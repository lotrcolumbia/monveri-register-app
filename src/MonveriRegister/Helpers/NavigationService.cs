using CommunityToolkit.Mvvm.ComponentModel;

namespace MonveriRegister.Helpers;

public partial class NavigationService : ObservableObject
{
    [ObservableProperty] private object? _currentViewModel;

    private readonly Dictionary<Type, Func<object>> _viewModelFactories = new();

    public void Register<T>(Func<T> factory) where T : class
    {
        _viewModelFactories[typeof(T)] = () => factory();
    }

    public void NavigateTo<T>() where T : class
    {
        if (_viewModelFactories.TryGetValue(typeof(T), out var factory))
            CurrentViewModel = factory();
    }

    public void NavigateTo(object viewModel)
    {
        CurrentViewModel = viewModel;
    }
}
