using System.Windows.Input;

namespace SolidPOS.PosCore.Wpf.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private string email = "admin@micafeteria.com";
    private string status = "Listo para login local offline.";

    public LoginViewModel()
    {
        ValidateCommand = new RelayCommand(() => Status = "LoginViewModel validado. La autenticación real vive en PosCore.Application.");
    }

    public string Email
    {
        get => email;
        set => SetProperty(ref email, value);
    }

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    public ICommand ValidateCommand { get; }
}
