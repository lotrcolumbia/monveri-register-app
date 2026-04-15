using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MonveriRegister.Helpers;
using MonveriRegister.Services;
using MonveriRegister.ViewModels;

namespace MonveriRegister;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Services
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IApiService, ApiService>();
        services.AddSingleton<ITaxService, TaxService>();
        services.AddSingleton<IDiscountService, DiscountService>();
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<ITransactionService, TransactionService>();
        services.AddSingleton<NavigationService>();

        // ViewModels
        services.AddTransient<LoginViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        // Initialize database
        var db = _serviceProvider.GetRequiredService<IDatabaseService>();
        db.Initialize();

        // Set up navigation
        var nav = _serviceProvider.GetRequiredService<NavigationService>();
        var loginVm = _serviceProvider.GetRequiredService<LoginViewModel>();
        nav.NavigateTo(loginVm);

        // Create and show main window
        var mainWindow = new MainWindow
        {
            DataContext = nav,
        };
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
