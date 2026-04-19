using System.Reactive;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using QCEDL.CLI.Core;
using QCEDL.CLI.Helpers;
using QCEDL.GUI.Services;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;
using ReactiveUI;

namespace QCEDL.GUI.ViewModels;

public sealed class ConnectionViewModel : ViewModelBase
{
    private readonly EdlService _service;
    private string? _loaderPath;
    private string _vidHex = "05C6";
    private string _pidHex = "9008";
    private StorageType _memoryType = StorageType.Ufs;
    private uint _slot;
    private string? _hostDevTarget;
    private string? _imgSize;
    private bool _radxaWos;
    private TransportBackend _backend = TransportBackend.Auto;
    private string? _serialDevicePath;
    private string _statusKey = "Conn_StatusNotConnected";
    private object?[] _statusArgs = [];
    private string _modeKey = "Conn_ModeUnknown";
    private string? _modeLiteral;
    private bool _isBusy;

    public ConnectionViewModel(EdlService service)
    {
        _service = service;

        var canRun = this.WhenAnyValue(x => x.IsBusy).Select(b => !b);

        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAsync, canRun);
        ProbeCommand = ReactiveCommand.CreateFromTask(ProbeAsync, canRun);
        DisconnectCommand = ReactiveCommand.Create(Disconnect, canRun);

        MemoryTypes = Enum.GetValues<StorageType>();
        Backends = Enum.GetValues<TransportBackend>();

        // Surface async errors as log entries rather than swallowing them.
        ConnectCommand.ThrownExceptions.Subscribe(ex =>
            Logging.Log(Localizer.Instance.Format("Conn_ConnectFailedFormat", ex.Message), LogLevel.Error));
        ProbeCommand.ThrownExceptions.Subscribe(ex =>
            Logging.Log(Localizer.Instance.Format("Conn_ProbeFailedFormat", ex.Message), LogLevel.Error));

        Localizer.Instance.CultureChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(ModeText));
        };
    }

    public IReadOnlyList<StorageType> MemoryTypes { get; }
    public IReadOnlyList<TransportBackend> Backends { get; }

    public bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool IsUnix { get; } = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public string? LoaderPath
    {
        get => _loaderPath;
        set => this.RaiseAndSetIfChanged(ref _loaderPath, value);
    }
    public string VidHex
    {
        get => _vidHex;
        set => this.RaiseAndSetIfChanged(ref _vidHex, value);
    }
    public string PidHex
    {
        get => _pidHex;
        set => this.RaiseAndSetIfChanged(ref _pidHex, value);
    }
    public StorageType MemoryType
    {
        get => _memoryType;
        set => this.RaiseAndSetIfChanged(ref _memoryType, value);
    }
    public uint Slot
    {
        get => _slot;
        set => this.RaiseAndSetIfChanged(ref _slot, value);
    }
    public string? HostDevTarget
    {
        get => _hostDevTarget;
        set => this.RaiseAndSetIfChanged(ref _hostDevTarget, value);
    }
    public string? ImgSize
    {
        get => _imgSize;
        set => this.RaiseAndSetIfChanged(ref _imgSize, value);
    }
    public bool RadxaWos
    {
        get => _radxaWos;
        set => this.RaiseAndSetIfChanged(ref _radxaWos, value);
    }
    public TransportBackend Backend
    {
        get => _backend;
        set
        {
            this.RaiseAndSetIfChanged(ref _backend, value);
            this.RaisePropertyChanged(nameof(IsSerialBackend));
            this.RaisePropertyChanged(nameof(IsUsbBackend));
        }
    }
    public bool IsSerialBackend => _backend == TransportBackend.Serial;
    public bool IsUsbBackend => _backend != TransportBackend.Serial;
    public string? SerialDevicePath
    {
        get => _serialDevicePath;
        set => this.RaiseAndSetIfChanged(ref _serialDevicePath, value);
    }

    public string StatusText => _statusArgs.Length == 0
        ? Localizer.Instance[_statusKey]
        : Localizer.Instance.Format(_statusKey, _statusArgs);

    public string ModeText => _modeLiteral ?? Localizer.Instance[_modeKey];

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }
    public ReactiveCommand<Unit, Unit> ProbeCommand { get; }
    public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }

    private void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private void SetModeKey(string key)
    {
        _modeKey = key;
        _modeLiteral = null;
        this.RaisePropertyChanged(nameof(ModeText));
    }

    private void SetModeLiteral(string literal)
    {
        _modeLiteral = literal;
        this.RaisePropertyChanged(nameof(ModeText));
    }

    private void ApplyOptions()
    {
        _service.Options = new EdlOptions
        {
            LoaderPath = LoaderPath,
            Vid = TryParseHex(VidHex),
            Pid = TryParseHex(PidHex),
            MemoryType = MemoryType,
            LogLevel = Logging.CurrentLogLevel,
            Slot = Slot,
            HostDevAsTarget = string.IsNullOrWhiteSpace(HostDevTarget) ? null : HostDevTarget,
            ImgSize = string.IsNullOrWhiteSpace(ImgSize) ? null : ImgSize,
            RadxaWosPlatform = RadxaWos && IsWindows,
            Backend = Backend,
            SerialDevicePath = Backend == TransportBackend.Serial && !string.IsNullOrWhiteSpace(SerialDevicePath)
                ? SerialDevicePath
                : null,
        };
        _service.ResetSession();
    }

    private async Task ProbeAsync()
    {
        IsBusy = true;
        try
        {
            ApplyOptions();
            Logging.Log(Localizer.Instance["Conn_LogProbing"], LogLevel.Info);
            var mode = await _service.ProbeAsync().ConfigureAwait(false);
            SetModeLiteral(mode.ToString());
            SetStatus("Conn_StatusDetectedFormat", mode);
        }
        finally { IsBusy = false; }
    }

    private async Task ConnectAsync()
    {
        IsBusy = true;
        try
        {
            ApplyOptions();
            if (_service.Options.HostDevAsTarget != null || _service.Options.RadxaWosPlatform)
            {
                Logging.Log(Localizer.Instance["Conn_LogDirectSession"], LogLevel.Info);
                SetStatus("Conn_StatusDirectReady");
                SetModeKey("Conn_ModeDirect");
                return;
            }

            Logging.Log(Localizer.Instance["Conn_LogConnecting"], LogLevel.Info);
            await _service.EnsureFirehoseAsync().ConfigureAwait(false);
            SetModeLiteral(_service.CurrentMode.ToString());
            SetStatus("Conn_StatusFirehoseConnected");
        }
        finally { IsBusy = false; }
    }

    private void Disconnect()
    {
        _service.ResetSession();
        SetStatus("Conn_StatusDisconnected");
        SetModeKey("Conn_ModeUnknown");
        Logging.Log(Localizer.Instance["Conn_LogSessionClosed"], LogLevel.Info);
    }

    private static int? TryParseHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        var trimmed = s.Trim().Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
        return int.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : null;
    }
}