using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MonveriRegister.Models;

namespace MonveriRegister.Services;

public class DatabaseService : IDatabaseService, IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public DatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "MonveriRegister");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "register.db");
    }

    private SqliteConnection GetConnection()
    {
        if (_connection == null)
        {
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
            cmd.ExecuteNonQuery();
        }
        return _connection;
    }

    public void Initialize()
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = Schema;
        cmd.ExecuteNonQuery();
    }

    #region Store Config

    public string? GetConfig(string key)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT value FROM store_config WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar()?.ToString();
    }

    public void SetConfig(string key, string value)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO store_config (key, value) VALUES (@key, @value)";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    #endregion

    #region Products

    public void UpsertProduct(Product p)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO products
            (product_id, sku, name, price, quantity, upc, category_id, category_name, unit_of_sale, price_per_unit, subtract, is_taxable, updated_at)
            VALUES (@id, @sku, @name, @price, @qty, @upc, @catid, @catname, @uos, @ppu, @sub, @tax, @updated)";
        cmd.Parameters.AddWithValue("@id", p.ProductId);
        cmd.Parameters.AddWithValue("@sku", p.Sku);
        cmd.Parameters.AddWithValue("@name", p.Name);
        cmd.Parameters.AddWithValue("@price", p.Price);
        cmd.Parameters.AddWithValue("@qty", p.Quantity);
        cmd.Parameters.AddWithValue("@upc", (object?)p.Upc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@catid", (object?)p.CategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@catname", (object?)p.CategoryName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uos", p.UnitOfSale);
        cmd.Parameters.AddWithValue("@ppu", p.PricePerUnit.HasValue ? p.PricePerUnit.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@sub", p.Subtract);
        cmd.Parameters.AddWithValue("@tax", p.IsTaxable);
        cmd.Parameters.AddWithValue("@updated", p.UpdatedAt);
        cmd.ExecuteNonQuery();
    }

    public void UpsertProducts(IEnumerable<Product> products)
    {
        var conn = GetConnection();
        using var transaction = conn.BeginTransaction();
        foreach (var p in products)
            UpsertProduct(p);
        transaction.Commit();
    }

    public Product? GetProductBySku(string sku)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM products WHERE sku = @sku LIMIT 1";
        cmd.Parameters.AddWithValue("@sku", sku);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }

    public Product? GetProductByUpc(string upc)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM products WHERE upc = @upc LIMIT 1";
        cmd.Parameters.AddWithValue("@upc", upc);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }

    public Product? GetVariantBySkuOrUpc(string code)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = @"SELECT pv.*, p.name AS parent_name, p.category_id, p.category_name,
                            COALESCE(pv.price, p.price) AS effective_price, p.unit_of_sale, p.price_per_unit
                            FROM product_variants pv
                            INNER JOIN products p ON pv.product_id = p.product_id
                            WHERE pv.sku = @code OR pv.upc = @code LIMIT 1";
        cmd.Parameters.AddWithValue("@code", code);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new Product
        {
            ProductId = reader.GetInt32(reader.GetOrdinal("product_id")),
            IsVariant = 1,
            VariantId = reader.GetInt32(reader.GetOrdinal("variant_id")),
            Sku = reader.GetString(reader.GetOrdinal("sku")),
            Upc = reader.IsDBNull(reader.GetOrdinal("upc")) ? null : reader.GetString(reader.GetOrdinal("upc")),
            VariantName = reader.IsDBNull(reader.GetOrdinal("variant_name")) ? null : reader.GetString(reader.GetOrdinal("variant_name")),
            VariantValue = reader.IsDBNull(reader.GetOrdinal("variant_value")) ? null : reader.GetString(reader.GetOrdinal("variant_value")),
            ParentName = reader.IsDBNull(reader.GetOrdinal("parent_name")) ? null : reader.GetString(reader.GetOrdinal("parent_name")),
            Name = reader.IsDBNull(reader.GetOrdinal("variant_name")) ? "" : reader.GetString(reader.GetOrdinal("variant_name")),
            Price = reader.GetDecimal(reader.GetOrdinal("effective_price")),
            Quantity = reader.GetInt32(reader.GetOrdinal("quantity")),
            CategoryId = reader.IsDBNull(reader.GetOrdinal("category_id")) ? null : reader.GetString(reader.GetOrdinal("category_id")),
            CategoryName = reader.IsDBNull(reader.GetOrdinal("category_name")) ? null : reader.GetString(reader.GetOrdinal("category_name")),
            UnitOfSale = reader.IsDBNull(reader.GetOrdinal("unit_of_sale")) ? "piece" : reader.GetString(reader.GetOrdinal("unit_of_sale")),
        };
    }

    public (string ParentSku, int QtyCount)? GetBarcodeRelationship(string barcode)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT parent_sku, qty_count FROM barcode_relationships WHERE scanned_barcode = @code LIMIT 1";
        cmd.Parameters.AddWithValue("@code", barcode);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return (reader.GetString(0), reader.GetInt32(1));
    }

    public void SaveBarcodeRelationships(IEnumerable<BarcodeRelationship> relationships)
    {
        var conn = GetConnection();
        using var transaction = conn.BeginTransaction();
        foreach (var rel in relationships)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT OR REPLACE INTO barcode_relationships (id, scanned_barcode, parent_sku, qty_count)
                VALUES (@id, @barcode, @sku, @qty)";
            cmd.Parameters.AddWithValue("@id", rel.Id);
            cmd.Parameters.AddWithValue("@barcode", rel.ScannedBarcode);
            cmd.Parameters.AddWithValue("@sku", rel.ParentSku);
            cmd.Parameters.AddWithValue("@qty", rel.QtyCount);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public Product? GetProductByPartialSku(string code)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM products WHERE sku LIKE @code ORDER BY LENGTH(sku) ASC LIMIT 1";
        cmd.Parameters.AddWithValue("@code", $"%{code}%");
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }

    public List<Product> SearchProducts(string query, int limit = 20)
    {
        var results = new List<Product>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = @"SELECT * FROM products WHERE
            name LIKE @q OR sku LIKE @q OR upc LIKE @q
            ORDER BY CASE
                WHEN sku = @exact THEN 0
                WHEN sku LIKE @starts THEN 1
                ELSE 2
            END, name ASC
            LIMIT @limit";
        cmd.Parameters.AddWithValue("@q", $"%{query}%");
        cmd.Parameters.AddWithValue("@exact", query);
        cmd.Parameters.AddWithValue("@starts", $"{query}%");
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadProduct(reader));
        return results;
    }

    private static Product ReadProduct(SqliteDataReader r)
    {
        return new Product
        {
            ProductId = r.GetInt32(r.GetOrdinal("product_id")),
            Sku = r.GetString(r.GetOrdinal("sku")),
            Name = r.GetString(r.GetOrdinal("name")),
            Price = r.GetDecimal(r.GetOrdinal("price")),
            Quantity = r.GetInt32(r.GetOrdinal("quantity")),
            Upc = r.IsDBNull(r.GetOrdinal("upc")) ? null : r.GetString(r.GetOrdinal("upc")),
            CategoryId = r.IsDBNull(r.GetOrdinal("category_id")) ? null : r.GetString(r.GetOrdinal("category_id")),
            CategoryName = r.IsDBNull(r.GetOrdinal("category_name")) ? null : r.GetString(r.GetOrdinal("category_name")),
            UnitOfSale = r.IsDBNull(r.GetOrdinal("unit_of_sale")) ? "piece" : r.GetString(r.GetOrdinal("unit_of_sale")),
            PricePerUnit = r.IsDBNull(r.GetOrdinal("price_per_unit")) ? null : r.GetDecimal(r.GetOrdinal("price_per_unit")),
            Subtract = r.GetInt32(r.GetOrdinal("subtract")),
            IsTaxable = r.GetInt32(r.GetOrdinal("is_taxable")),
            UpdatedAt = r.GetString(r.GetOrdinal("updated_at")),
        };
    }

    #endregion

    #region Customers

    public void UpsertCustomer(Customer c)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = @"INSERT OR REPLACE INTO customers
            (customer_id, fname, lname, company, phone1, email, loyalty_card_number, current_points, tier_name, updated_at)
            VALUES (@id, @fn, @ln, @co, @ph, @em, @lc, @cp, @tn, @up)";
        cmd.Parameters.AddWithValue("@id", c.CustomerId);
        cmd.Parameters.AddWithValue("@fn", (object?)c.FirstName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ln", (object?)c.LastName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@co", (object?)c.Company ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ph", (object?)c.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@em", (object?)c.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@lc", (object?)c.LoyaltyCardNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cp", c.CurrentPoints);
        cmd.Parameters.AddWithValue("@tn", (object?)c.TierName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@up", c.UpdatedAt);
        cmd.ExecuteNonQuery();
    }

    public void UpsertCustomers(IEnumerable<Customer> customers)
    {
        var conn = GetConnection();
        using var transaction = conn.BeginTransaction();
        foreach (var c in customers) UpsertCustomer(c);
        transaction.Commit();
    }

    public Customer? GetCustomerById(int id)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM customers WHERE customer_id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadCustomer(reader) : null;
    }

    public List<Customer> SearchCustomers(string query, int limit = 20)
    {
        var results = new List<Customer>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = @"SELECT * FROM customers WHERE
            fname LIKE @q OR lname LIKE @q OR company LIKE @q OR phone1 LIKE @q OR email LIKE @q OR loyalty_card_number LIKE @q
            ORDER BY lname, fname LIMIT @limit";
        cmd.Parameters.AddWithValue("@q", $"%{query}%");
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) results.Add(ReadCustomer(reader));
        return results;
    }

    private static Customer ReadCustomer(SqliteDataReader r)
    {
        return new Customer
        {
            CustomerId = r.GetInt32(r.GetOrdinal("customer_id")),
            FirstName = r.IsDBNull(r.GetOrdinal("fname")) ? null : r.GetString(r.GetOrdinal("fname")),
            LastName = r.IsDBNull(r.GetOrdinal("lname")) ? null : r.GetString(r.GetOrdinal("lname")),
            Company = r.IsDBNull(r.GetOrdinal("company")) ? null : r.GetString(r.GetOrdinal("company")),
            Phone = r.IsDBNull(r.GetOrdinal("phone1")) ? null : r.GetString(r.GetOrdinal("phone1")),
            Email = r.IsDBNull(r.GetOrdinal("email")) ? null : r.GetString(r.GetOrdinal("email")),
            LoyaltyCardNumber = r.IsDBNull(r.GetOrdinal("loyalty_card_number")) ? null : r.GetString(r.GetOrdinal("loyalty_card_number")),
            CurrentPoints = r.GetInt32(r.GetOrdinal("current_points")),
            TierName = r.IsDBNull(r.GetOrdinal("tier_name")) ? null : r.GetString(r.GetOrdinal("tier_name")),
            UpdatedAt = r.GetString(r.GetOrdinal("updated_at")),
        };
    }

    #endregion

    #region Tax Locations

    public void SaveTaxLocations(IEnumerable<TaxLocation> locations)
    {
        var conn = GetConnection();
        using var transaction = conn.BeginTransaction();

        using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM tax_location_rates"; del.ExecuteNonQuery(); }
        using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM tax_locations"; del.ExecuteNonQuery(); }

        foreach (var loc in locations)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO tax_locations (id, location_name, tax_rate, is_default) VALUES (@id, @name, @rate, @def)";
            cmd.Parameters.AddWithValue("@id", loc.Id);
            cmd.Parameters.AddWithValue("@name", loc.LocationName);
            cmd.Parameters.AddWithValue("@rate", loc.TaxRate);
            cmd.Parameters.AddWithValue("@def", loc.IsDefault);
            cmd.ExecuteNonQuery();

            foreach (var rate in loc.Rates)
            {
                using var rcmd = conn.CreateCommand();
                rcmd.CommandText = @"INSERT INTO tax_location_rates (id, location_id, tax_type_name, rate) VALUES (@id, @lid, @name, @rate)";
                rcmd.Parameters.AddWithValue("@id", rate.Id);
                rcmd.Parameters.AddWithValue("@lid", loc.Id);
                rcmd.Parameters.AddWithValue("@name", rate.TaxTypeName);
                rcmd.Parameters.AddWithValue("@rate", rate.Rate);
                rcmd.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public TaxLocation? GetTaxLocation(int id)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM tax_locations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var loc = ReadTaxLocation(reader);
        loc.Rates = GetTaxRates(id);
        return loc;
    }

    public TaxLocation? GetDefaultTaxLocation()
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM tax_locations WHERE is_default = 1 LIMIT 1";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var loc = ReadTaxLocation(reader);
        loc.Rates = GetTaxRates(loc.Id);
        return loc;
    }

    public List<TaxLocation> GetAllTaxLocations()
    {
        var results = new List<TaxLocation>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM tax_locations ORDER BY location_name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var loc = ReadTaxLocation(reader);
            loc.Rates = GetTaxRates(loc.Id);
            results.Add(loc);
        }
        return results;
    }

    private List<TaxRate> GetTaxRates(int locationId)
    {
        var rates = new List<TaxRate>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM tax_location_rates WHERE location_id = @id";
        cmd.Parameters.AddWithValue("@id", locationId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rates.Add(new TaxRate
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                LocationId = reader.GetInt32(reader.GetOrdinal("location_id")),
                TaxTypeName = reader.GetString(reader.GetOrdinal("tax_type_name")),
                Rate = reader.GetDecimal(reader.GetOrdinal("rate")),
            });
        }
        return rates;
    }

    private static TaxLocation ReadTaxLocation(SqliteDataReader r)
    {
        return new TaxLocation
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            LocationName = r.GetString(r.GetOrdinal("location_name")),
            TaxRate = r.GetDecimal(r.GetOrdinal("tax_rate")),
            IsDefault = r.GetInt32(r.GetOrdinal("is_default")),
        };
    }

    #endregion

    #region Employees

    public void SaveEmployees(IEnumerable<Employee> employees)
    {
        var conn = GetConnection();
        using var transaction = conn.BeginTransaction();
        using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM employees"; del.ExecuteNonQuery(); }
        foreach (var e in employees)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO employees (id, name, username, pin_hash, level, status) VALUES (@id, @name, @user, @pin, @lvl, @status)";
            cmd.Parameters.AddWithValue("@id", e.Id);
            cmd.Parameters.AddWithValue("@name", e.Name);
            cmd.Parameters.AddWithValue("@user", (object?)e.Username ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@pin", e.PinHash);
            cmd.Parameters.AddWithValue("@lvl", e.Level);
            cmd.Parameters.AddWithValue("@status", e.Status);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public Employee? GetEmployeeById(int id)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM employees WHERE id = @id AND status = 'active'";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new Employee
        {
            Id = reader.GetInt32(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Username = reader.IsDBNull(reader.GetOrdinal("username")) ? null : reader.GetString(reader.GetOrdinal("username")),
            PinHash = reader.GetString(reader.GetOrdinal("pin_hash")),
            Level = reader.GetInt32(reader.GetOrdinal("level")),
            Status = reader.GetString(reader.GetOrdinal("status")),
        };
    }

    public List<Employee> GetAllEmployees()
    {
        var results = new List<Employee>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM employees WHERE status = 'active' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Employee
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Username = reader.IsDBNull(reader.GetOrdinal("username")) ? null : reader.GetString(reader.GetOrdinal("username")),
                PinHash = reader.GetString(reader.GetOrdinal("pin_hash")),
                Level = reader.GetInt32(reader.GetOrdinal("level")),
                Status = reader.GetString(reader.GetOrdinal("status")),
            });
        }
        return results;
    }

    #endregion

    #region Register Sessions

    public int InsertSession(RegisterSession s)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = @"INSERT INTO register_sessions
            (server_id, employee_id, employee_name, opened_at, opening_cash, opening_breakdown, status, site_id, is_training, tax_location_id, is_synced)
            VALUES (@sid, @eid, @ename, @opened, @cash, @breakdown, @status, @site, @training, @taxloc, @synced);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@sid", s.ServerId.HasValue ? s.ServerId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@eid", s.EmployeeId);
        cmd.Parameters.AddWithValue("@ename", s.EmployeeName);
        cmd.Parameters.AddWithValue("@opened", s.OpenedAt);
        cmd.Parameters.AddWithValue("@cash", s.OpeningCash);
        cmd.Parameters.AddWithValue("@breakdown", (object?)s.OpeningBreakdown ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", s.Status);
        cmd.Parameters.AddWithValue("@site", s.SiteId.HasValue ? s.SiteId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@training", s.IsTraining ? 1 : 0);
        cmd.Parameters.AddWithValue("@taxloc", s.TaxLocationId.HasValue ? s.TaxLocationId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@synced", s.IsSynced ? 1 : 0);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void UpdateSession(RegisterSession s)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = @"UPDATE register_sessions SET
            closed_at = @closed, closing_cash = @cash, closing_breakdown = @breakdown,
            status = @status, notes = @notes, is_synced = @synced, server_id = @sid
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", s.Id);
        cmd.Parameters.AddWithValue("@closed", (object?)s.ClosedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cash", s.ClosingCash.HasValue ? s.ClosingCash.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@breakdown", (object?)s.ClosingBreakdown ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", s.Status);
        cmd.Parameters.AddWithValue("@notes", (object?)s.Notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@synced", s.IsSynced ? 1 : 0);
        cmd.Parameters.AddWithValue("@sid", s.ServerId.HasValue ? s.ServerId.Value : DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public RegisterSession? GetOpenSession(int employeeId)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM register_sessions WHERE employee_id = @eid AND status = 'open' LIMIT 1";
        cmd.Parameters.AddWithValue("@eid", employeeId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadSession(reader) : null;
    }

    private static RegisterSession ReadSession(SqliteDataReader r)
    {
        return new RegisterSession
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            ServerId = r.IsDBNull(r.GetOrdinal("server_id")) ? null : r.GetInt32(r.GetOrdinal("server_id")),
            EmployeeId = r.GetInt32(r.GetOrdinal("employee_id")),
            EmployeeName = r.GetString(r.GetOrdinal("employee_name")),
            OpenedAt = r.GetString(r.GetOrdinal("opened_at")),
            ClosedAt = r.IsDBNull(r.GetOrdinal("closed_at")) ? null : r.GetString(r.GetOrdinal("closed_at")),
            OpeningCash = r.GetDecimal(r.GetOrdinal("opening_cash")),
            OpeningBreakdown = r.IsDBNull(r.GetOrdinal("opening_breakdown")) ? null : r.GetString(r.GetOrdinal("opening_breakdown")),
            ClosingCash = r.IsDBNull(r.GetOrdinal("closing_cash")) ? null : r.GetDecimal(r.GetOrdinal("closing_cash")),
            ClosingBreakdown = r.IsDBNull(r.GetOrdinal("closing_breakdown")) ? null : r.GetString(r.GetOrdinal("closing_breakdown")),
            Status = r.GetString(r.GetOrdinal("status")),
            SiteId = r.IsDBNull(r.GetOrdinal("site_id")) ? null : r.GetInt32(r.GetOrdinal("site_id")),
            IsTraining = r.GetInt32(r.GetOrdinal("is_training")) == 1,
            TaxLocationId = r.IsDBNull(r.GetOrdinal("tax_location_id")) ? null : r.GetInt32(r.GetOrdinal("tax_location_id")),
            IsSynced = r.GetInt32(r.GetOrdinal("is_synced")) == 1,
        };
    }

    #endregion

    #region Transactions

    public int InsertTransaction(Transaction t)
    {
        var conn = GetConnection();
        using var txn = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO transactions
            (server_id, employee_id, customer_id, type, payment, payment_detail, subtotal, discount, discount_type,
             tax, tax_rate, tax_location_id, service_fee, service_fee_tax, total, tendered, change_amount,
             timestamp, suspended_at, suspended_note, is_split_payment, receipt_token, session_id, original_ticket_id, is_synced)
            VALUES (@sid, @eid, @cid, @type, @pay, @paydet, @sub, @disc, @dtype, @tax, @taxrate, @taxloc,
                    @sfee, @sfeetax, @total, @tend, @change, @ts, @susat, @susnote, @issplit, @receipt, @sessid, @origid, @synced);
            SELECT last_insert_rowid();";
        AddTransactionParams(cmd, t);
        var id = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var item in t.Items)
        {
            item.TransactionId = id;
            InsertTransactionItem(conn, item);
        }

        foreach (var sp in t.SplitPayments)
        {
            sp.TransactionId = id;
            InsertSplitPayment(conn, sp);
        }

        txn.Commit();
        return id;
    }

    private static void InsertTransactionItem(SqliteConnection conn, TransactionItem item)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO transaction_items
            (transaction_id, employee_id, sku, name, upc, price, qty, override_price, cost,
             is_variant, variant_id, is_bundle, bundle_id, is_service, service_id, unit_of_sale, measured_qty, qty_multiplier)
            VALUES (@tid, @eid, @sku, @name, @upc, @price, @qty, @oprice, @cost,
                    @isvar, @varid, @isbun, @bunid, @isser, @serid, @uos, @mqty, @qmul)";
        cmd.Parameters.AddWithValue("@tid", item.TransactionId);
        cmd.Parameters.AddWithValue("@eid", item.EmployeeId);
        cmd.Parameters.AddWithValue("@sku", item.Sku);
        cmd.Parameters.AddWithValue("@name", item.Name);
        cmd.Parameters.AddWithValue("@upc", (object?)item.Upc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@price", item.Price);
        cmd.Parameters.AddWithValue("@qty", item.Qty);
        cmd.Parameters.AddWithValue("@oprice", item.OverridePrice.HasValue ? item.OverridePrice.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@cost", item.Cost.HasValue ? item.Cost.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@isvar", item.IsVariant ? 1 : 0);
        cmd.Parameters.AddWithValue("@varid", item.VariantId.HasValue ? item.VariantId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@isbun", item.IsBundle ? 1 : 0);
        cmd.Parameters.AddWithValue("@bunid", item.BundleId.HasValue ? item.BundleId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@isser", item.IsService ? 1 : 0);
        cmd.Parameters.AddWithValue("@serid", item.ServiceId.HasValue ? item.ServiceId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@uos", item.UnitOfSale);
        cmd.Parameters.AddWithValue("@mqty", item.MeasuredQty.HasValue ? item.MeasuredQty.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@qmul", item.QtyMultiplier);
        cmd.ExecuteNonQuery();
    }

    private static void InsertSplitPayment(SqliteConnection conn, SplitPayment sp)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO split_payments (transaction_id, payment_method, amount, reference)
            VALUES (@tid, @method, @amount, @ref)";
        cmd.Parameters.AddWithValue("@tid", sp.TransactionId);
        cmd.Parameters.AddWithValue("@method", sp.PaymentMethod);
        cmd.Parameters.AddWithValue("@amount", sp.Amount);
        cmd.Parameters.AddWithValue("@ref", (object?)sp.Reference ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void UpdateTransaction(Transaction t)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = @"UPDATE transactions SET
            type = @type, payment = @pay, payment_detail = @paydet, subtotal = @sub, discount = @disc, discount_type = @dtype,
            tax = @tax, tax_rate = @taxrate, tax_location_id = @taxloc, service_fee = @sfee, service_fee_tax = @sfeetax,
            total = @total, tendered = @tend, change_amount = @change, suspended_at = @susat, suspended_note = @susnote,
            is_split_payment = @issplit, receipt_token = @receipt, is_synced = @synced, server_id = @sid, customer_id = @cid
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", t.Id);
        AddTransactionParams(cmd, t);
        cmd.ExecuteNonQuery();
    }

    private static void AddTransactionParams(SqliteCommand cmd, Transaction t)
    {
        cmd.Parameters.AddWithValue("@sid", t.ServerId.HasValue ? t.ServerId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@eid", t.EmployeeId);
        cmd.Parameters.AddWithValue("@cid", t.CustomerId.HasValue ? t.CustomerId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@type", t.Type);
        cmd.Parameters.AddWithValue("@pay", (object?)t.Payment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@paydet", (object?)t.PaymentDetail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sub", t.Subtotal);
        cmd.Parameters.AddWithValue("@disc", t.Discount);
        cmd.Parameters.AddWithValue("@dtype", (object?)t.DiscountType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tax", t.Tax);
        cmd.Parameters.AddWithValue("@taxrate", t.TaxRate);
        cmd.Parameters.AddWithValue("@taxloc", t.TaxLocationId.HasValue ? t.TaxLocationId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@sfee", t.ServiceFee);
        cmd.Parameters.AddWithValue("@sfeetax", t.ServiceFeeTax);
        cmd.Parameters.AddWithValue("@total", t.Total);
        cmd.Parameters.AddWithValue("@tend", t.Tendered.HasValue ? t.Tendered.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@change", t.ChangeAmount.HasValue ? t.ChangeAmount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@ts", t.Timestamp);
        cmd.Parameters.AddWithValue("@susat", (object?)t.SuspendedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@susnote", (object?)t.SuspendedNote ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@issplit", t.IsSplitPayment ? 1 : 0);
        cmd.Parameters.AddWithValue("@receipt", (object?)t.ReceiptToken ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sessid", t.SessionId.HasValue ? t.SessionId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@origid", t.OriginalTicketId.HasValue ? t.OriginalTicketId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@synced", t.IsSynced ? 1 : 0);
    }

    public Transaction? GetTransaction(int id)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM transactions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var t = ReadTransaction(reader);
        t.Items = GetTransactionItems(id);
        t.SplitPayments = GetSplitPayments(id);
        return t;
    }

    public List<Transaction> GetSuspendedTransactions()
    {
        var results = new List<Transaction>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM transactions WHERE type = 'Suspended' ORDER BY suspended_at DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var t = ReadTransaction(reader);
            t.Items = GetTransactionItems(t.Id);
            results.Add(t);
        }
        return results;
    }

    public Transaction? GetActiveTransaction(int employeeId)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM transactions WHERE employee_id = @eid AND type = 'Active' ORDER BY timestamp DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@eid", employeeId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var t = ReadTransaction(reader);
        t.Items = GetTransactionItems(t.Id);
        return t;
    }

    private List<TransactionItem> GetTransactionItems(int transactionId)
    {
        var items = new List<TransactionItem>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM transaction_items WHERE transaction_id = @tid";
        cmd.Parameters.AddWithValue("@tid", transactionId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new TransactionItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                TransactionId = reader.GetInt32(reader.GetOrdinal("transaction_id")),
                EmployeeId = reader.GetInt32(reader.GetOrdinal("employee_id")),
                Sku = reader.GetString(reader.GetOrdinal("sku")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Upc = reader.IsDBNull(reader.GetOrdinal("upc")) ? null : reader.GetString(reader.GetOrdinal("upc")),
                Price = reader.GetDecimal(reader.GetOrdinal("price")),
                Qty = reader.GetInt32(reader.GetOrdinal("qty")),
                OverridePrice = reader.IsDBNull(reader.GetOrdinal("override_price")) ? null : reader.GetDecimal(reader.GetOrdinal("override_price")),
                Cost = reader.IsDBNull(reader.GetOrdinal("cost")) ? null : reader.GetDecimal(reader.GetOrdinal("cost")),
                IsVariant = reader.GetInt32(reader.GetOrdinal("is_variant")) == 1,
                VariantId = reader.IsDBNull(reader.GetOrdinal("variant_id")) ? null : reader.GetInt32(reader.GetOrdinal("variant_id")),
                IsBundle = reader.GetInt32(reader.GetOrdinal("is_bundle")) == 1,
                BundleId = reader.IsDBNull(reader.GetOrdinal("bundle_id")) ? null : reader.GetInt32(reader.GetOrdinal("bundle_id")),
                IsService = reader.GetInt32(reader.GetOrdinal("is_service")) == 1,
                ServiceId = reader.IsDBNull(reader.GetOrdinal("service_id")) ? null : reader.GetInt32(reader.GetOrdinal("service_id")),
                UnitOfSale = reader.GetString(reader.GetOrdinal("unit_of_sale")),
                MeasuredQty = reader.IsDBNull(reader.GetOrdinal("measured_qty")) ? null : reader.GetDecimal(reader.GetOrdinal("measured_qty")),
                QtyMultiplier = reader.GetInt32(reader.GetOrdinal("qty_multiplier")),
            });
        }
        return items;
    }

    private List<SplitPayment> GetSplitPayments(int transactionId)
    {
        var payments = new List<SplitPayment>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM split_payments WHERE transaction_id = @tid";
        cmd.Parameters.AddWithValue("@tid", transactionId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            payments.Add(new SplitPayment
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                TransactionId = reader.GetInt32(reader.GetOrdinal("transaction_id")),
                PaymentMethod = reader.GetString(reader.GetOrdinal("payment_method")),
                Amount = reader.GetDecimal(reader.GetOrdinal("amount")),
                Reference = reader.IsDBNull(reader.GetOrdinal("reference")) ? null : reader.GetString(reader.GetOrdinal("reference")),
            });
        }
        return payments;
    }

    private static Transaction ReadTransaction(SqliteDataReader r)
    {
        return new Transaction
        {
            Id = r.GetInt32(r.GetOrdinal("id")),
            ServerId = r.IsDBNull(r.GetOrdinal("server_id")) ? null : r.GetInt32(r.GetOrdinal("server_id")),
            EmployeeId = r.GetInt32(r.GetOrdinal("employee_id")),
            CustomerId = r.IsDBNull(r.GetOrdinal("customer_id")) ? null : r.GetInt32(r.GetOrdinal("customer_id")),
            Type = r.GetString(r.GetOrdinal("type")),
            Payment = r.IsDBNull(r.GetOrdinal("payment")) ? null : r.GetString(r.GetOrdinal("payment")),
            PaymentDetail = r.IsDBNull(r.GetOrdinal("payment_detail")) ? null : r.GetString(r.GetOrdinal("payment_detail")),
            Subtotal = r.GetDecimal(r.GetOrdinal("subtotal")),
            Discount = r.GetDecimal(r.GetOrdinal("discount")),
            DiscountType = r.IsDBNull(r.GetOrdinal("discount_type")) ? null : r.GetString(r.GetOrdinal("discount_type")),
            Tax = r.GetDecimal(r.GetOrdinal("tax")),
            TaxRate = r.GetDecimal(r.GetOrdinal("tax_rate")),
            TaxLocationId = r.IsDBNull(r.GetOrdinal("tax_location_id")) ? null : r.GetInt32(r.GetOrdinal("tax_location_id")),
            ServiceFee = r.GetDecimal(r.GetOrdinal("service_fee")),
            ServiceFeeTax = r.GetDecimal(r.GetOrdinal("service_fee_tax")),
            Total = r.GetDecimal(r.GetOrdinal("total")),
            Tendered = r.IsDBNull(r.GetOrdinal("tendered")) ? null : r.GetDecimal(r.GetOrdinal("tendered")),
            ChangeAmount = r.IsDBNull(r.GetOrdinal("change_amount")) ? null : r.GetDecimal(r.GetOrdinal("change_amount")),
            Timestamp = r.GetString(r.GetOrdinal("timestamp")),
            SuspendedAt = r.IsDBNull(r.GetOrdinal("suspended_at")) ? null : r.GetString(r.GetOrdinal("suspended_at")),
            SuspendedNote = r.IsDBNull(r.GetOrdinal("suspended_note")) ? null : r.GetString(r.GetOrdinal("suspended_note")),
            IsSplitPayment = r.GetInt32(r.GetOrdinal("is_split_payment")) == 1,
            ReceiptToken = r.IsDBNull(r.GetOrdinal("receipt_token")) ? null : r.GetString(r.GetOrdinal("receipt_token")),
            SessionId = r.IsDBNull(r.GetOrdinal("session_id")) ? null : r.GetInt32(r.GetOrdinal("session_id")),
            OriginalTicketId = r.IsDBNull(r.GetOrdinal("original_ticket_id")) ? null : r.GetInt32(r.GetOrdinal("original_ticket_id")),
            IsSynced = r.GetInt32(r.GetOrdinal("is_synced")) == 1,
        };
    }

    #endregion

    #region Discounts

    public void SaveDiscounts(IEnumerable<Discount> discounts)
    {
        var conn = GetConnection();
        using var transaction = conn.BeginTransaction();
        using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM discounts"; del.ExecuteNonQuery(); }
        foreach (var d in discounts)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO discounts (id, name, type, discount_value, min_spend, buy_quantity, get_quantity, category_id, sku, free_sku, stackable, bxgy_target_mode, category_ids)
                VALUES (@id, @name, @type, @dv, @ms, @bq, @gq, @cid, @sku, @fsku, @stack, @mode, @cids)";
            cmd.Parameters.AddWithValue("@id", d.Id);
            cmd.Parameters.AddWithValue("@name", d.Name);
            cmd.Parameters.AddWithValue("@type", d.Type);
            cmd.Parameters.AddWithValue("@dv", d.DiscountValue);
            cmd.Parameters.AddWithValue("@ms", d.MinSpend);
            cmd.Parameters.AddWithValue("@bq", d.BuyQuantity);
            cmd.Parameters.AddWithValue("@gq", d.GetQuantity);
            cmd.Parameters.AddWithValue("@cid", d.CategoryId.HasValue ? d.CategoryId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@sku", (object?)d.Sku ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fsku", (object?)d.FreeSku ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@stack", d.Stackable);
            cmd.Parameters.AddWithValue("@mode", d.BxgyTargetMode);
            cmd.Parameters.AddWithValue("@cids", (object?)d.CategoryIds ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public List<Discount> GetAllDiscounts()
    {
        var results = new List<Discount>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM discounts";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Discount
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Type = reader.GetString(reader.GetOrdinal("type")),
                DiscountValue = reader.GetDecimal(reader.GetOrdinal("discount_value")),
                MinSpend = reader.GetDecimal(reader.GetOrdinal("min_spend")),
                BuyQuantity = reader.GetInt32(reader.GetOrdinal("buy_quantity")),
                GetQuantity = reader.GetInt32(reader.GetOrdinal("get_quantity")),
                CategoryId = reader.IsDBNull(reader.GetOrdinal("category_id")) ? null : reader.GetInt32(reader.GetOrdinal("category_id")),
                Sku = reader.IsDBNull(reader.GetOrdinal("sku")) ? null : reader.GetString(reader.GetOrdinal("sku")),
                FreeSku = reader.IsDBNull(reader.GetOrdinal("free_sku")) ? null : reader.GetString(reader.GetOrdinal("free_sku")),
                Stackable = reader.GetInt32(reader.GetOrdinal("stackable")),
                BxgyTargetMode = reader.GetString(reader.GetOrdinal("bxgy_target_mode")),
                CategoryIds = reader.IsDBNull(reader.GetOrdinal("category_ids")) ? null : reader.GetString(reader.GetOrdinal("category_ids")),
            });
        }
        return results;
    }

    #endregion

    #region Categories

    public void SaveCategories(IEnumerable<Category> categories)
    {
        var conn = GetConnection();
        using var transaction = conn.BeginTransaction();
        using (var del = conn.CreateCommand()) { del.CommandText = "DELETE FROM categories"; del.ExecuteNonQuery(); }
        foreach (var c in categories)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO categories (category_id, name, parent_id) VALUES (@id, @name, @pid)";
            cmd.Parameters.AddWithValue("@id", c.CategoryId);
            cmd.Parameters.AddWithValue("@name", c.Name);
            cmd.Parameters.AddWithValue("@pid", (object?)c.ParentId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public List<Category> GetAllCategories()
    {
        var results = new List<Category>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM categories ORDER BY name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Category
            {
                CategoryId = reader.GetString(reader.GetOrdinal("category_id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                ParentId = reader.IsDBNull(reader.GetOrdinal("parent_id")) ? null : reader.GetString(reader.GetOrdinal("parent_id")),
            });
        }
        return results;
    }

    public string? GetProductCategoryId(string sku)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT category_id FROM products WHERE sku = @sku LIMIT 1";
        cmd.Parameters.AddWithValue("@sku", sku);
        return cmd.ExecuteScalar()?.ToString();
    }

    public List<Product> GetProductsByCategory(string categoryId, int limit = 50)
    {
        var results = new List<Product>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM products WHERE category_id = @cid ORDER BY name LIMIT @limit";
        cmd.Parameters.AddWithValue("@cid", categoryId);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadProduct(reader));
        return results;
    }

    #endregion

    #region Sync Queue

    public void EnqueueSync(SyncQueueItem item)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = @"INSERT INTO sync_queue (entity_type, entity_id, action, payload, created_at, status)
            VALUES (@type, @eid, @action, @payload, @created, 'pending')";
        cmd.Parameters.AddWithValue("@type", item.EntityType);
        cmd.Parameters.AddWithValue("@eid", item.EntityId);
        cmd.Parameters.AddWithValue("@action", item.Action);
        cmd.Parameters.AddWithValue("@payload", item.Payload);
        cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public List<SyncQueueItem> GetPendingSyncItems(int limit = 50)
    {
        var results = new List<SyncQueueItem>();
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "SELECT * FROM sync_queue WHERE status = 'pending' ORDER BY created_at ASC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new SyncQueueItem
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                EntityType = reader.GetString(reader.GetOrdinal("entity_type")),
                EntityId = reader.GetInt32(reader.GetOrdinal("entity_id")),
                Action = reader.GetString(reader.GetOrdinal("action")),
                Payload = reader.GetString(reader.GetOrdinal("payload")),
                CreatedAt = reader.GetString(reader.GetOrdinal("created_at")),
                Attempts = reader.GetInt32(reader.GetOrdinal("attempts")),
                LastAttempt = reader.IsDBNull(reader.GetOrdinal("last_attempt")) ? null : reader.GetString(reader.GetOrdinal("last_attempt")),
                LastError = reader.IsDBNull(reader.GetOrdinal("last_error")) ? null : reader.GetString(reader.GetOrdinal("last_error")),
                Status = reader.GetString(reader.GetOrdinal("status")),
            });
        }
        return results;
    }

    public void MarkSynced(int id)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "UPDATE sync_queue SET status = 'synced', last_attempt = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void MarkSyncFailed(int id, string error)
    {
        using var cmd = GetConnection().CreateCommand();
        cmd.CommandText = "UPDATE sync_queue SET attempts = attempts + 1, last_attempt = @now, last_error = @err WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("@err", error);
        cmd.ExecuteNonQuery();
    }

    #endregion

    public void Dispose()
    {
        _connection?.Dispose();
    }

    #region Schema

    private const string Schema = @"
CREATE TABLE IF NOT EXISTS store_config (
    key    TEXT PRIMARY KEY,
    value  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS products (
    product_id    INTEGER PRIMARY KEY,
    sku           TEXT NOT NULL UNIQUE,
    name          TEXT NOT NULL,
    price         REAL NOT NULL DEFAULT 0,
    quantity      INTEGER DEFAULT 0,
    upc           TEXT,
    category_id   TEXT,
    category_name TEXT,
    unit_of_sale  TEXT DEFAULT 'piece',
    price_per_unit REAL,
    subtract      INTEGER DEFAULT 1,
    is_taxable    INTEGER DEFAULT 1,
    updated_at    TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS product_variants (
    variant_id    INTEGER PRIMARY KEY,
    product_id    INTEGER NOT NULL,
    sku           TEXT NOT NULL UNIQUE,
    upc           TEXT,
    variant_name  TEXT,
    variant_value TEXT,
    price         REAL,
    quantity      INTEGER DEFAULT 0,
    updated_at    TEXT NOT NULL,
    FOREIGN KEY (product_id) REFERENCES products(product_id)
);

CREATE TABLE IF NOT EXISTS barcode_relationships (
    id              INTEGER PRIMARY KEY,
    scanned_barcode TEXT NOT NULL UNIQUE,
    parent_sku      TEXT NOT NULL,
    qty_count       INTEGER DEFAULT 1
);

CREATE TABLE IF NOT EXISTS customers (
    customer_id       INTEGER PRIMARY KEY,
    fname             TEXT,
    lname             TEXT,
    company           TEXT,
    phone1            TEXT,
    email             TEXT,
    loyalty_card_number TEXT,
    current_points    INTEGER DEFAULT 0,
    tier_name         TEXT,
    updated_at        TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS register_sessions (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    server_id         INTEGER,
    employee_id       INTEGER NOT NULL,
    employee_name     TEXT NOT NULL,
    opened_at         TEXT NOT NULL,
    closed_at         TEXT,
    opening_cash      REAL DEFAULT 0,
    opening_breakdown TEXT,
    closing_cash      REAL,
    closing_breakdown TEXT,
    status            TEXT DEFAULT 'open',
    site_id           INTEGER,
    is_training       INTEGER DEFAULT 0,
    tax_location_id   INTEGER,
    notes             TEXT,
    is_synced         INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS transactions (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    server_id         INTEGER,
    employee_id       INTEGER NOT NULL,
    customer_id       INTEGER,
    type              TEXT NOT NULL DEFAULT 'Active',
    payment           TEXT,
    payment_detail    TEXT,
    subtotal          REAL DEFAULT 0,
    discount          REAL DEFAULT 0,
    discount_type     TEXT,
    tax               REAL DEFAULT 0,
    tax_rate          REAL DEFAULT 0,
    tax_location_id   INTEGER,
    service_fee       REAL DEFAULT 0,
    service_fee_tax   REAL DEFAULT 0,
    total             REAL DEFAULT 0,
    tendered          REAL,
    change_amount     REAL,
    timestamp         TEXT NOT NULL,
    suspended_at      TEXT,
    suspended_note    TEXT,
    is_split_payment  INTEGER DEFAULT 0,
    receipt_token     TEXT,
    session_id        INTEGER,
    original_ticket_id INTEGER,
    is_synced         INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS transaction_items (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    transaction_id  INTEGER NOT NULL,
    employee_id     INTEGER NOT NULL,
    sku             TEXT NOT NULL,
    name            TEXT NOT NULL,
    upc             TEXT,
    price           REAL NOT NULL,
    qty             INTEGER NOT NULL DEFAULT 1,
    override_price  REAL,
    cost            REAL,
    is_variant      INTEGER DEFAULT 0,
    variant_id      INTEGER,
    is_bundle       INTEGER DEFAULT 0,
    bundle_id       INTEGER,
    is_service      INTEGER DEFAULT 0,
    service_id      INTEGER,
    unit_of_sale    TEXT DEFAULT 'piece',
    measured_qty    REAL,
    qty_multiplier  INTEGER DEFAULT 1,
    FOREIGN KEY (transaction_id) REFERENCES transactions(id)
);

CREATE TABLE IF NOT EXISTS split_payments (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    transaction_id  INTEGER NOT NULL,
    payment_method  TEXT NOT NULL,
    amount          REAL NOT NULL,
    reference       TEXT,
    FOREIGN KEY (transaction_id) REFERENCES transactions(id)
);

CREATE TABLE IF NOT EXISTS tax_locations (
    id            INTEGER PRIMARY KEY,
    location_name TEXT NOT NULL,
    tax_rate      REAL NOT NULL,
    is_default    INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS tax_location_rates (
    id            INTEGER PRIMARY KEY,
    location_id   INTEGER NOT NULL,
    tax_type_name TEXT NOT NULL,
    rate          REAL NOT NULL,
    FOREIGN KEY (location_id) REFERENCES tax_locations(id)
);

CREATE TABLE IF NOT EXISTS employees (
    id       INTEGER PRIMARY KEY,
    name     TEXT NOT NULL,
    username TEXT,
    pin_hash TEXT NOT NULL,
    level    INTEGER DEFAULT 0,
    status   TEXT DEFAULT 'active'
);

CREATE TABLE IF NOT EXISTS sync_queue (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    entity_type     TEXT NOT NULL,
    entity_id       INTEGER NOT NULL,
    action          TEXT NOT NULL,
    payload         TEXT NOT NULL,
    created_at      TEXT NOT NULL,
    attempts        INTEGER DEFAULT 0,
    last_attempt    TEXT,
    last_error      TEXT,
    status          TEXT DEFAULT 'pending'
);

CREATE INDEX IF NOT EXISTS idx_products_upc ON products(upc);
CREATE INDEX IF NOT EXISTS idx_products_name ON products(name);
CREATE INDEX IF NOT EXISTS idx_variants_sku ON product_variants(sku);
CREATE INDEX IF NOT EXISTS idx_variants_upc ON product_variants(upc);
CREATE INDEX IF NOT EXISTS idx_barcode_scanned ON barcode_relationships(scanned_barcode);
CREATE INDEX IF NOT EXISTS idx_transactions_type ON transactions(type);
CREATE INDEX IF NOT EXISTS idx_transactions_employee ON transactions(employee_id);
CREATE INDEX IF NOT EXISTS idx_sync_status ON sync_queue(status);

CREATE TABLE IF NOT EXISTS discounts (
    id              INTEGER PRIMARY KEY,
    name            TEXT NOT NULL,
    type            TEXT NOT NULL,
    discount_value  REAL DEFAULT 0,
    min_spend       REAL DEFAULT 0,
    buy_quantity    INTEGER DEFAULT 0,
    get_quantity    INTEGER DEFAULT 0,
    category_id     INTEGER,
    sku             TEXT,
    free_sku        TEXT,
    stackable       INTEGER DEFAULT 0,
    bxgy_target_mode TEXT DEFAULT 'product',
    category_ids    TEXT
);

CREATE TABLE IF NOT EXISTS categories (
    category_id     TEXT PRIMARY KEY,
    name            TEXT NOT NULL,
    parent_id       TEXT
);
CREATE INDEX IF NOT EXISTS idx_categories_parent ON categories(parent_id);
";

    #endregion
}
