using System.Diagnostics;
using Android.App;
using EduFocus.Parent.Core.Push;
using Org.Unifiedpush.Android.Connector;
using Org.Unifiedpush.Android.Connector.Data;
using Application = Android.App.Application;
using UnifiedPushConnector = Org.Unifiedpush.Android.Connector.UnifiedPush;

namespace EduFocus.Parent.App.Services;

public enum UnifiedPushAvailability
{
    Ready,
    NeedsSelection,
    NoDistributor,
}

/// <summary>
/// Small wrapper around the UnifiedPush connector. The distributor owns the
/// endpoint; the app only asks for registration and receives callbacks through
/// <see cref="UnifiedPushPushService"/>.
/// </summary>
public sealed class UnifiedPushManager
{
    public const string InstanceName = "edufocus";
    private static readonly TimeSpan DistributorSelectionTimeout = TimeSpan.FromMinutes(2);
    private const string RegistrationMessage = "EduFocus gửi cảnh báo và báo cáo học tập.";

    public UnifiedPushAvailability GetAvailability()
    {
        try
        {
            return UnifiedPushConnector.ResolveDefaultDistributor(Application.Context) switch
            {
                ResolvedDistributor.Found => UnifiedPushAvailability.Ready,
                ResolvedDistributor.NoneAvailable => UnifiedPushAvailability.NoDistributor,
                _ => UnifiedPushAvailability.NeedsSelection,
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UnifiedPush availability check failed: {ex.GetType().Name}");
            return UnifiedPushAvailability.NoDistributor;
        }
    }

    public bool EnsureRegistered(string? vapidPublicKey)
    {
        try
        {
            if (UnifiedPushConnector.ResolveDefaultDistributor(Application.Context)
                is not ResolvedDistributor.Found)
            {
                return false;
            }

            UnifiedPushConnector.Register(
                Application.Context,
                InstanceName,
                RegistrationMessage,
                vapidPublicKey);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UnifiedPush registration failed: {ex.GetType().Name}");
            return false;
        }
    }

    public async Task<bool> RequestDistributorAsync(string? vapidPublicKey)
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
            return false;

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            UnifiedPushConnector.TryUseCurrentOrDefaultDistributor(
                activity,
                new BooleanCallback(selected =>
                {
                    if (selected)
                        EnsureRegistered(vapidPublicKey);

                    completion.TrySetResult(selected);
                }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UnifiedPush distributor selection failed: {ex.GetType().Name}");
            completion.TrySetResult(false);
        }

        try
        {
            return await completion.Task.WaitAsync(DistributorSelectionTimeout);
        }
        catch (TimeoutException)
        {
            Debug.WriteLine("UnifiedPush distributor selection timed out.");
            return false;
        }
    }

    public void Unregister()
    {
        try
        {
            UnifiedPushConnector.Unregister(Application.Context, InstanceName);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UnifiedPush unregister failed: {ex.GetType().Name}");
        }
    }

    private sealed class BooleanCallback(Action<bool> callback)
        : Java.Lang.Object, Kotlin.Jvm.Functions.IFunction1
    {
        public Java.Lang.Object? Invoke(Java.Lang.Object? p0)
        {
            callback(p0 is Java.Lang.Boolean value && value.BooleanValue());
            return null;
        }
    }
}

