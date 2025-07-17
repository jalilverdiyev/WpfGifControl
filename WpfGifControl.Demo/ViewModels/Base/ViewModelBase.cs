using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace WpfGifControl.Demo.ViewModels.Base;

    public class ViewModelBase : IModelView, INotifyPropertyChanged
    {
        public ViewModelBase()
        {
            //App.OnCloseLauncher += CloseLauncher;
            //App.OnRemoteConfigLoaded += RemoteConfigLoaded;
            //if (App.Config != null)
            //    RemoteConfigLoaded();

            //UserConfig.OnLoggedIn += UserLogIn;
            //if (UserConfig.IsUserLoggedIn)
            //    UserLogIn();
            //UserConfig.OnLoggedOut += UserLogOut;
        }

        [DllImport("KERNEL32.DLL", EntryPoint = "SetProcessWorkingSetSize", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern bool SetProcessWorkingSetSize(IntPtr pProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

        [DllImport("KERNEL32.DLL", EntryPoint = "GetCurrentProcess", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern IntPtr GetCurrentProcess();

        public Type ModelType => this.GetType();

        public ICommand PageLoaded
        {
            get
            {
                return new RelayCommand((res) =>
                {
                    Loaded(res);
                });
            }
        }

        public ICommand PageUnloaded
        {
            get
            {
                return new RelayCommand((res) =>
                {
                    Unloaded();
                });
            }
        }

        public virtual void Loaded(object res) { }

        public virtual void Unloaded()
        {
            IntPtr pHandle = GetCurrentProcess();
            SetProcessWorkingSetSize(pHandle, -1, -1);
        }

        public virtual void Initialize(params object[] args) { }

        public virtual void OpenStarted() { }
        public virtual void Open()
        {
            OpenStarted();
            OpenCompleted();
        }
        public virtual void OpenCompleted() { }

        public virtual void CloseStarted() { }
        public virtual void Close()
        {
            CloseStarted();
            CloseCompleted();
        }
        public virtual void CloseCompleted() { }

        public virtual void Reset() { }

        /// <summary>
        /// The event that is fired when any child property changes its value
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged = (sender, e) => { };

        /// <summary>
        /// Call this to fire a <see cref="PropertyChanged"/> event
        /// </summary>
        /// <param name="name"></param>
        public void OnPropertyChanged(string name)
        {
            PropertyChanged(this, new PropertyChangedEventArgs(name));
        }
    }
