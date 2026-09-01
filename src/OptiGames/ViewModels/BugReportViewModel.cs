using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;
using OptiGames.Core.Services;

namespace OptiGames.ViewModels;

public sealed class AttachmentViewModel : ObservableObject
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileName(Path);
    public string SizeText => DriveCleanupService.FormatBytes(new FileInfo(Path).Length);
    public required RelayCommand RemoveCommand { get; init; }
}

/// <summary>
/// The in-app bug report. Files straight into the same support inbox the website uses, so a report
/// is a thread staff already have tooling for rather than an email nobody triages.
///
/// The point of doing this in the app rather than opening a browser is the diagnostics: the app
/// already knows the hardware, the Windows build, which tweaks are applied and what it just did.
/// A report that carries those is actionable; "it broke" is not. They are attached as text the
/// user can read before sending, because silently uploading a machine profile would be the kind
/// of thing this tool exists to not do.
/// </summary>
public sealed class BugReportViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly SupportService _support;
    private readonly SystemInfoProvider _info = new();

    public BugReportViewModel(MainViewModel main, SupportService support)
    {
        _main = main;
        _support = support;

        AttachCommand = new RelayCommand(PickFiles, () => !IsSending);
        SubmitCommand = new RelayCommand(async () => await SubmitAsync(), () => CanSubmit);
        CloseCommand = new RelayCommand(() => _main.CloseBugReport());
        DoneCommand = new RelayCommand(() =>
        {
            _main.CloseBugReport();
            Reset();
        });
    }

    public RelayCommand AttachCommand { get; }
    public RelayCommand SubmitCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand DoneCommand { get; }

    public ObservableCollection<AttachmentViewModel> Attachments { get; } = new();

    // ---- Fields ----

    private string _email = "";
    public string Email
    {
        get => _email;
        set { if (Set(ref _email, value)) RaiseValidity(); }
    }

    private string _message = "";
    public string Message
    {
        get => _message;
        set { if (Set(ref _message, value)) RaiseValidity(); }
    }

    private bool _includeDiagnostics = true;
    public bool IncludeDiagnostics
    {
        get => _includeDiagnostics;
        set { if (Set(ref _includeDiagnostics, value)) Raise(nameof(DiagnosticsText)); }
    }

    private bool _showDiagnostics;
    public bool ShowDiagnostics { get => _showDiagnostics; set => Set(ref _showDiagnostics, value); }

    // ---- State ----

    private bool _isSending;
    public bool IsSending
    {
        get => _isSending;
        private set
        {
            if (!Set(ref _isSending, value)) return;
            Raise(nameof(IsEditable));
            RaiseValidity();
        }
    }

    public bool IsEditable => !IsSending && !IsSent;

    private bool _isSent;
    public bool IsSent
    {
        get => _isSent;
        private set
        {
            if (!Set(ref _isSent, value)) return;
            Raise(nameof(IsEditable));
            RaiseValidity();
        }
    }

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private string? _error;
    public string? Error
    {
        get => _error;
        private set { if (Set(ref _error, value)) Raise(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>
    /// The server requires a real address — it replies there if the user closes the app — so this
    /// is validated here too rather than letting a typo come back as a 400 after upload.
    /// </summary>
    public bool EmailLooksValid
    {
        get
        {
            var e = Email.Trim();
            int at = e.IndexOf('@');
            int dot = e.LastIndexOf('.');
            return at > 0 && dot > at + 1 && dot < e.Length - 1 && !e.Contains(' ');
        }
    }

    public bool CanSubmit =>
        !IsSending && !IsSent && EmailLooksValid && Message.Trim().Length >= 10;

    private void RaiseValidity()
    {
        Raise(nameof(EmailLooksValid));
        Raise(nameof(CanSubmit));
        RelayCommand.RaiseCanExecuteChanged();
    }

    // ---- Diagnostics ----

    /// <summary>
    /// What gets appended to the report. Built on demand so it reflects the machine at the moment
    /// of sending, and shown in the UI so nothing leaves without the user being able to read it.
    /// </summary>
    public string DiagnosticsText
    {
        get
        {
            if (!IncludeDiagnostics) return "";

            var i = _info.Get();
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"OptiGames {version}");
            sb.AppendLine($"Windows: {i.Windows}");
            sb.AppendLine($"Motherboard: {i.Motherboard}");
            sb.AppendLine($"CPU: {i.Cpu}");
            sb.AppendLine($"GPU: {i.Gpu}");
            sb.AppendLine($"RAM: {i.Memory}");
            sb.AppendLine($"Tweaks applied: {_main.Optimize.AppliedCount} of {_main.Optimize.TotalCount}");

            var applied = _main.Optimize.Groups
                .SelectMany(g => g.Items)
                .Where(t => t.IsApplied)
                .Select(t => t.Name)
                .ToList();
            if (applied.Count > 0) sb.AppendLine($"  {string.Join(", ", applied)}");

            // The last stretch of the action log is usually the whole story: it names the exact
            // registry writes that immediately preceded whatever went wrong.
            var log = _main.LogText;
            if (!string.IsNullOrWhiteSpace(log))
            {
                var lines = log.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var tail = lines.Skip(Math.Max(0, lines.Length - 40));
                sb.AppendLine();
                sb.AppendLine("Recent actions:");
                foreach (var line in tail) sb.AppendLine($"  {line}");
            }

            return sb.ToString().TrimEnd();
        }
    }

    // ---- Attachments ----

    private void PickFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Attach screenshots",
            Multiselect = true,
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp;*.gif)|*.png;*.jpg;*.jpeg;*.webp;*.gif",
        };

        if (dialog.ShowDialog() != true) return;

        foreach (var path in dialog.FileNames)
        {
            if (Attachments.Any(a => a.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;

            if (!SupportService.IsAllowedImage(path))
            {
                Error = $"{Path.GetFileName(path)} is not an image we can accept.";
                continue;
            }

            if (new FileInfo(path).Length > SupportService.MaxAttachmentBytes)
            {
                Error = $"{Path.GetFileName(path)} is over 8MB. Resize it first.";
                continue;
            }

            AttachmentViewModel? item = null;
            item = new AttachmentViewModel
            {
                Path = path,
                RemoveCommand = new RelayCommand(() => Attachments.Remove(item!)),
            };
            Attachments.Add(item);
        }

        Raise(nameof(HasAttachments));
    }

    public bool HasAttachments => Attachments.Count > 0;

    // ---- Send ----

    private async Task SubmitAsync()
    {
        IsSending = true;
        Error = null;

        var ids = new List<string>();
        for (int i = 0; i < Attachments.Count; i++)
        {
            Status = $"Uploading image {i + 1} of {Attachments.Count}…";
            var uploaded = await _support.UploadAsync(Attachments[i].Path);

            // A refused image should not silently vanish from a report the user believes carries
            // it, so this stops rather than sending a thread with the evidence missing.
            if (uploaded is null)
            {
                Error = $"{Attachments[i].Name} could not be uploaded. Remove it and try again.";
                Status = "";
                IsSending = false;
                return;
            }
            ids.Add(uploaded.Id);
        }

        Status = "Sending…";

        var body = Message.Trim();
        var diagnostics = DiagnosticsText;
        if (diagnostics.Length > 0) body += "\n\n" + diagnostics;

        var result = await _support.SubmitAsync(Email.Trim(), body, ids);

        IsSending = false;
        Status = "";

        if (!result.Ok)
        {
            Error = result.Error ?? "Could not send the report.";
            return;
        }

        IsSent = true;
        _main.Home.RefreshSummaries();
    }

    /// <summary>Clears the form so the next report does not open pre-filled with the last one.</summary>
    public void Reset()
    {
        Message = "";
        Attachments.Clear();
        Raise(nameof(HasAttachments));
        Error = null;
        Status = "";
        IsSent = false;
        ShowDiagnostics = false;
        // Email is deliberately kept: it is the same person next time.
    }
}
