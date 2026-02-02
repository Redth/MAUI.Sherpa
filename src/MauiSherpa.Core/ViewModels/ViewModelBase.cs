using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MauiSherpa.Core.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected Interfaces.IAlertService AlertService { get; }
    protected Interfaces.ILoggingService Logger { get; }

    protected ViewModelBase(Interfaces.IAlertService alertService, Interfaces.ILoggingService loggingService)
    {
        AlertService = alertService;
        Logger = loggingService;
    }

    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public virtual Task CleanupAsync()
    {
        return Task.CompletedTask;
    }
}
