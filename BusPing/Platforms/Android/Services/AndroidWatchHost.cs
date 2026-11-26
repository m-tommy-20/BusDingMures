using Android.OS;
using AndroidX.Core.Content;
using BusPing.Services.Interfaces;
using Microsoft.Maui.ApplicationModel;

namespace BusPing.Services.Android;

/// <summary>Starts and stops the foreground service that evaluates watches.</summary>
public sealed class AndroidWatchHost : IWatchHost
{
    public bool IsRunning => BusWatchForegroundService.IsRunning;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
            {
                await Permissions.RequestAsync<Permissions.PostNotifications>();
            }
        }

        AndroidNotificationService.EnsureChannels();

        var context = Platform.AppContext;
        var intent = new global::Android.Content.Intent(context, typeof(BusWatchForegroundService));
        intent.SetAction(BusWatchForegroundService.ActionStart);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            ContextCompat.StartForegroundService(context, intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        var context = Platform.AppContext;
        context.StopService(
            new global::Android.Content.Intent(context, typeof(BusWatchForegroundService)));
        return Task.CompletedTask;
    }
}
