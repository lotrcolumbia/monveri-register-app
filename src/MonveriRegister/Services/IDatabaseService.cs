using MonveriRegister.Models;

namespace MonveriRegister.Services;

public interface IDatabaseService
{
    void Initialize();

    // Store config
    string? GetConfig(string key);
    void SetConfig(string key, string value);

    // Products
    void UpsertProduct(Product product);
    void UpsertProducts(IEnumerable<Product> products);
    Product? GetProductBySku(string sku);
    Product? GetProductByUpc(string upc);
    Product? GetVariantBySkuOrUpc(string code);
    (string ParentSku, int QtyCount)? GetBarcodeRelationship(string barcode);
    void SaveBarcodeRelationships(IEnumerable<BarcodeRelationship> relationships);
    Product? GetProductByPartialSku(string code);
    List<Product> SearchProducts(string query, int limit = 20);

    // Customers
    void UpsertCustomer(Customer customer);
    void UpsertCustomers(IEnumerable<Customer> customers);
    Customer? GetCustomerById(int id);
    List<Customer> SearchCustomers(string query, int limit = 20);

    // Tax
    void SaveTaxLocations(IEnumerable<TaxLocation> locations);
    TaxLocation? GetTaxLocation(int id);
    TaxLocation? GetDefaultTaxLocation();
    List<TaxLocation> GetAllTaxLocations();

    // Employees
    void SaveEmployees(IEnumerable<Employee> employees);
    Employee? GetEmployeeById(int id);
    List<Employee> GetAllEmployees();

    // Discounts
    void SaveDiscounts(IEnumerable<Discount> discounts);
    List<Discount> GetAllDiscounts();

    // Categories
    void SaveCategories(IEnumerable<Category> categories);
    List<Category> GetAllCategories();
    string? GetProductCategoryId(string sku);
    List<Product> GetProductsByCategory(string categoryId, int limit = 50);

    // Register sessions
    int InsertSession(RegisterSession session);
    void UpdateSession(RegisterSession session);
    RegisterSession? GetOpenSession(int employeeId);

    // Transactions
    int InsertTransaction(Transaction transaction);
    void UpdateTransaction(Transaction transaction);
    Transaction? GetTransaction(int id);
    List<Transaction> GetSuspendedTransactions();
    Transaction? GetActiveTransaction(int employeeId);

    // Sync queue
    void EnqueueSync(SyncQueueItem item);
    List<SyncQueueItem> GetPendingSyncItems(int limit = 50);
    void MarkSynced(int id);
    void MarkSyncFailed(int id, string error);
}
