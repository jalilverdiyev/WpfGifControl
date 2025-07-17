using System.Windows;
using System.Windows.Input;

namespace WpfGifControl.Demo.ViewModels.Base;

public class RelayCommand : ICommand
{
	public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
	{
		_execute = execute;
		_canExecute = canExecute;
	}

	private Action<object?> _execute;
	private Func<object?, bool>? _canExecute;

	public UIElement CommandElement;

	public event EventHandler? CanExecuteChanged
	{
		add { CommandManager.RequerySuggested += value; }
		remove { CommandManager.RequerySuggested -= value; }
	}

	public bool CanExecute(object? parameter)
	{
		return _canExecute == null || _canExecute(parameter);
	}

	public void Execute(object? parameter)
	{
		_execute(parameter);
	}
}
