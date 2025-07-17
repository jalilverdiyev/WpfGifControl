using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using WpfGifControl.AvaloniaBase;
using WpfGifControl.Demo.ViewModels.Base;

namespace WpfGifControl.Demo.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
	public ObservableCollection<GifSourceSelectItem> SourceItems { get; set; }

	public int SelectedSource { get; set; }

	public string SelectedSourceName { get; set; }

	public object GifSource { get; set; }

	public IterationCount IterationCount { get; set; } = IterationCount.Infinite;

	public uint SpecialCount { get; set; } = 5;

	public MainWindowViewModel()
	{
		SourceItems = new ObservableCollection<GifSourceSelectItem>()
		{
				new()
				{
						SourceName = "Scream(shortUrl)",
						Source = "/Resources/Images/scream.gif"
				},
				new()
				{
						SourceName = "Scream(fullUrl)",
						Source = "pack://application:,,,/WpfGifControl.Demo;component/Resources/Images/scream.gif"
				},
				new()
				{
						SourceName = "Tired Tom(https url)",
						Source =
								"https://media3.giphy.com/media/v1.Y2lkPTc5MGI3NjExMXRnYjc3YzBqcHM1Zno5MHc5ZmRvdGIxNTR5Y3preXF3MDVrMHM1dSZlcD12MV9pbnRlcm5hbF9naWZfYnlfaWQmY3Q9Zw/bEs40jYsdQjmM/giphy.gif"
				},
				new()
				{
						SourceName = "Not gif",
						Source = "Resources/Images/not-gif.png"
				},
				new()
				{
						SourceName = "Non animated",
						Source = "Resources/Images/nonanimated.gif"
				}
		};

		SelectedSource = 0;
		GifSource = SourceItems[SelectedSource].Source;
		SelectedSourceName = SourceItems[SelectedSource].SourceName;
	}

	public ICommand ReplayGif => new RelayCommand(res =>
	{
		if (res is not MouseEventArgs { Source: GifControl gifControl })
			return;

		gifControl.BeginReplay();
	});

	public ICommand StopGif => new RelayCommand(res =>
	{
		if (res is not MouseEventArgs { Source: GifControl gifControl })
			return;

		gifControl.EndReplay();
	});

	public ICommand SelectSource => new RelayCommand(_ =>
	{
		GifSource = SourceItems[SelectedSource].Source;
		SelectedSourceName = SourceItems[SelectedSource].SourceName;
	});

	public ICommand CheckInfinite => new RelayCommand(_ => { IterationCount = IterationCount.Infinite; });

	public ICommand CheckSpecial => new RelayCommand(_ => { IterationCount = new IterationCount(SpecialCount); });

	public ICommand PlayGif => new RelayCommand(res =>
	{
		if (res is not GifControl gifControl)
			return;

		gifControl.BeginReplay();
	});

	public ICommand PauseGif => new RelayCommand(res =>
	{
		if (res is not GifControl gifControl)
			return;

		gifControl.Stop();
	});
}

public class GifSourceSelectItem
{
	public string SourceName { get; set; } = null!;
	public object Source { get; set; } = null!;
}
