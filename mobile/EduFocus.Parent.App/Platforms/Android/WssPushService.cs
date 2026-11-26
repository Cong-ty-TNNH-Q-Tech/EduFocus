using System.Diagnostics;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core.Push;
using Application = Android.App.Application;
using Debug = System.Diagnostics.Debug;
using OperationCanceledException = System.OperationCanceledException;

namespace EduFocus.Parent.App;

[Service(
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeRemoteMessaging)]
public sealed class WssPushService : Service
{
    public const string ActionRestart = "edufocus.action.WSS_RESTART";
    private const string ChannelId = "edufocus_connection";
    private const int NotificationId = 41001;
    private static int _running;

    private readonly object _loopGate = new();
    private CancellationTokenSource? _cancellation;
    private bool _loopRunning;

    public static event Action? StateChanged;

    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    public static void Start(bool restart = false)
    {
        if (!restart && IsRunning)
        {
            return;
        }

        try
        {
            var context = Application.Context;
            var intent = new Intent(context, typeof(WssPushService));
            if (restart)
            {
                intent.SetAction(ActionRestart);
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                context.StartForegroundService(intent);
            }
            else
            {
                context.StartService(intent);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Push stream service start failed: {exception.GetType().Name}");
        }
    }

    public static void Stop()
    {
        try
        {
            Application.Context.StopService(new Intent(
                Application.Context,
                typeof(WssPushService)));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Push stream service stop failed: {exception.GetType().Name}");
        }
    }

    public override StartCommandResult OnStartCommand(
        Intent? intent,
        StartCommandFlags flags,
        int startId)
    {
        try
        {
            EnsureChannel();
            var notification = BuildNotification();
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
            {
                StartForeground(
                    NotificationId,
                    notification,
                    ForegroundService.TypeRemoteMessaging);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Push stream service foreground failed: {exception.GetType().Name}");
            Interlocked.Exchange(ref _running, 0);
            StateChanged?.Invoke();
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }

        var restartRequested = string.Equals(intent?.Action, ActionRestart, StringComparison.Ordinal);
        CancellationTokenSource? previousCts = null;
        CancellationTokenSource? startCts = null;

        lock (_loopGate)
        {
            if (restartRequested && _loopRunning)
            {
                previousCts = _cancellation;
                _cancellation = null;
                _loopRunning = false;
            }

            if (!_loopRunning)
            {
                startCts = new CancellationTokenSource();
                _cancellation = startCts;
                _loopRunning = true;
                Interlocked.Exchange(ref _running, 1);
            }
        }

        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            previousCts.Dispose();
        }

        if (startCts is not null)
        {
            StateChanged?.Invoke();
            _ = RunAsync(startCts, startId);
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        CancellationTokenSource? cts;
        lock (_loopGate)
        {
            cts = _cancellation;
            _cancellation = null;
            _loopRunning = false;
            Interlocked.Exchange(ref _running, 0);
        }

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            cts.Dispose();
        }

        StateChanged?.Invoke();
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private async Task RunAsync(CancellationTokenSource cts, int startId)
    {
        var cancellationToken = cts.Token;
        try
        {
            var client = ServiceHelper.GetService<IPushStreamClient>();
            var handler = ServiceHelper.GetService<PushMessageHandler>();
            await client.RunAsync(handler.Handle, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Push stream service stopped: {exception.GetType().Name}");
        }
        finally
        {
            var wasCancelled = true;
            try
            {
                wasCancelled = cts.IsCancellationRequested;
            }
            catch (ObjectDisposedException)
            {
            }

            var isCurrent = false;
            lock (_loopGate)
            {
                if (ReferenceEquals(_cancellation, cts))
                {
                    _cancellation = null;
                    _loopRunning = false;
                    Interlocked.Exchange(ref _running, 0);
                    isCurrent = true;
                }
            }

            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            if (isCurrent)
            {
                StateChanged?.Invoke();
                if (!wasCancelled)
                {
                    StopSelf(startId);
                }
            }
        }
    }

    private void EnsureChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null)
        {
            return;
        }

        var channel = new NotificationChannel(
            ChannelId,
            "Kết nối thông báo",
            NotificationImportance.Low)
        {
            Description = "Duy trì kết nối nhận thông báo từ máy tính.",
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification()
    {
        var launchIntent = new Intent(this, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var builder = new Notification.Builder(this, ChannelId)
            .SetContentTitle("EduFocus đang nhận thông báo")
            .SetContentText("Đang duy trì kết nối để nhận cảnh báo từ máy tính.")
            .SetSmallIcon(Resource.Drawable.icon_bell)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .SetCategory(Notification.CategoryService)
            .SetShowWhen(false);

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            builder.SetForegroundServiceBehavior(
                (int)NotificationForegroundService.Immediate);
        }

        return builder.Build();
    }
}
