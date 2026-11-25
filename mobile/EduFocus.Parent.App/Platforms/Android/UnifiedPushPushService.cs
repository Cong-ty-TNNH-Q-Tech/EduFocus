using System.Diagnostics;
using Android.App;
using EduFocus.Parent.App.Services;
using EduFocus.Parent.Core.Push;
using Org.Unifiedpush.Android.Connector;
using Org.Unifiedpush.Android.Connector.Data;

namespace EduFocus.Parent.App;

/// <summary>
/// Receives distributor callbacks. Android can start this service while the
/// process is dead, so every callback resolves dependencies from MAUI and
/// fails closed instead of throwing into the binder thread.
/// </summary>
[Service(Exported = false)]
[IntentFilter(new[] { PushService.ActionPushEvent })]
public sealed class UnifiedPushPushService : PushService
{
    public override void OnMessage(PushMessage message, string instance)
    {
        try
        {
            if (!message.Decrypted)
                return;

            ServiceHelper.GetService<PushMessageHandler>().Handle(message.GetContent());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UnifiedPush message failed: {ex.GetType().Name}");
        }
    }

    public override void OnNewEndpoint(PushEndpoint endpoint, string instance)
    {
        try
        {
            var keys = endpoint.PubKeySet;
            if (keys is null
                || string.IsNullOrWhiteSpace(endpoint.Url)
                || string.IsNullOrWhiteSpace(keys.PubKey)
                || string.IsNullOrWhiteSpace(keys.Auth))
            {
                return;
            }

            ServiceHelper.GetService<PushCoordinator>().OnEndpointChanged(
                new PushEndpointRegistration(endpoint.Url, keys.PubKey, keys.Auth));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UnifiedPush endpoint failed: {ex.GetType().Name}");
        }
    }

    public override void OnRegistrationFailed(FailedReason reason, string instance)
    {
        try
        {
            Debug.WriteLine($"UnifiedPush registration failed: {reason}");
            ServiceHelper.GetService<PushCoordinator>().OnRegistrationFailed();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UnifiedPush registration failure callback failed: {ex.GetType().Name}");
        }
    }

    public override void OnUnregistered(string instance)
    {
        try
        {
            ServiceHelper.GetService<PushCoordinator>().OnUnregistered();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UnifiedPush unregister callback failed: {ex.GetType().Name}");
        }
    }

    public override void OnTempUnavailable(string instance) =>
        Debug.WriteLine("UnifiedPush temporarily unavailable.");
}
