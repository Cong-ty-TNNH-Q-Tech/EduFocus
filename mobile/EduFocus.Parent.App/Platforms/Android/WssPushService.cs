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
    private const string ChannelId = "edufocus_connection";
    private const int NotificationId = 41001;
    private static int _running;

    private CancellationTokenSource? _cancellation;

    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    public static void Start()
    {
        try
        {
            var context = Application.Context;
            var intent = new Intent(context, typeof(WssPushService));
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
        if (Interlocked.CompareExchange(ref _running, 1, 0) == 1)
        {
            return StartCommandResult.Sticky;
        }

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

            _cancellation = new CancellationTokenSource();
            _ = RunAsync(_cancellation.Token);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Push stream service foreground failed: {exception.GetType().Name}");
            Interlocked.Exchange(ref _running, 0);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        Interlocked.Exchange(ref _running, 0);
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private async Task RunAsync(CancellationToken cancellationToken)
    {
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
            if (!cancellationToken.IsCancellationRequested)
            {
                StopSelf();
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
