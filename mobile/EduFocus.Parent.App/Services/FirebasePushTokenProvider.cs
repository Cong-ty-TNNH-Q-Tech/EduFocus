#if FCM_ENABLED
using EduFocus.Parent.Core.Push;
using Plugin.Firebase.CloudMessaging;

namespace EduFocus.Parent.App.Services;

public sealed class FirebasePushTokenProvider : IPushTokenProvider
{
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
#endif
