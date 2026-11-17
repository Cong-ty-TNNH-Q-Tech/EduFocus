using System.Globalization;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using EduFocus.Parent.Core.Push;
using Application = Android.App.Application;
using Notification = Android.App.Notification;
using NotificationManager = Android.App.NotificationManager;

namespace EduFocus.Parent.App.Services;

/// <summary>
/// Creates the high-importance <c>edufocus_alerts</c> channel and shows safe,
/// generic notifications. Foreground messages become an in-app toast instead
/// of a second system notification.
/// </summary>
public sealed class PushNotificationPresenter
{
    public const string ChannelId = "edufocus_alerts";
    private const string ChannelName = "Cảnh báo EduFocus";

    public void EnsureChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
            return;

        var manager = (NotificationManager?)Application.Context.GetSystemService(Context.NotificationService);
        if (manager is null)
            return;

        var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.High)
        {
            Description = "Cảnh báo và báo cáo tổng hợp từ máy tính.",
        };
        channel.EnableVibration(true);
        manager.CreateNotificationChannel(channel);
    }

    public void Present(PushNotificationPayload payload, bool foreground)
    {
        var content = PushNotificationContentFactory.Create(payload.Type);
        if (foreground)
        {
            ShowInAppBanner(content);
            return;
        }

        try
        {
            ShowSystemNotification(payload, content);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Push notification display failed: {ex.GetType().Name}");
        }
    }

    private static void ShowInAppBanner(PushNotificationContent content) =>
        MainThread.BeginInvokeOnMainThread(() =>
            Android.Widget.Toast.MakeText(
                Application.Context,
                $"{content.Title}. {content.Body}",
                Android.Widget.ToastLength.Long)?.Show());

    private static void ShowSystemNotification(
        PushNotificationPayload payload,
        PushNotificationContent content)
    {
        var context = Application.Context;
        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (manager is null)
            return;

        var intent = new Intent(context, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        intent.PutExtra(PushTapRouter.TypeExtra, payload.TypeKey);
        if (payload.LocalDate is { } date)
        {
            intent.PutExtra(
                PushTapRouter.DateExtra,
                date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        var notificationId = PushNotificationId.FromKey(payload.DedupeKey);
        var pendingIntent = PendingIntent.GetActivity(
            context,
            notificationId,
            intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var builder = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentTitle(content.Title)
            .SetContentText(content.Body)
            .SetStyle(new Notification.BigTextStyle().BigText(content.Body))
            .SetAutoCancel(true)
            .SetContentIntent(pendingIntent)
            .SetCategory(Notification.CategoryStatus)
            .SetVisibility(NotificationVisibility.Private);

        manager.Notify(notificationId, builder.Build());
    }
}
