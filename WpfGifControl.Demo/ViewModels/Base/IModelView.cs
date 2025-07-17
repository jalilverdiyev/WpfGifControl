namespace WpfGifControl.Demo.ViewModels.Base;

public interface IModelView
{
	void Loaded(object res);
	void Unloaded();
	void Initialize(params object[] res);
	void OpenStarted();
	void Open();
	void OpenCompleted();
	void CloseStarted();
	void Close();
	void CloseCompleted();
	void Reset();
}
