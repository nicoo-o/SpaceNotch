using System;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Features;
using Windows.ApplicationModel.Appointments;

namespace SpaceNotch.Platform.Windows.Calendar;

/// <summary>Un rendez-vous lu dans le calendrier de Windows.</summary>
public sealed record CalendarMeeting(string Id, string Subject, DateTimeOffset Start, DateTimeOffset End, string? Location, string? Details, string? OnlineLink);

/// <summary>
/// Prochain rendez-vous (F2) : lecture seule du calendrier de Windows
/// (Calendrier, Outlook synchronisé) par <see cref="AppointmentStore"/>. Il
/// faut l'identité de paquet et la capacité « appointments » ; sans elles,
/// la lecture échoue proprement et la fonctionnalité reste muette.
/// </summary>
public sealed class CalendarReader
{
    private AppointmentStore? _store;

    // Une seule ouverture du magasin à la fois : deux lectures simultanées
    // abonnaient deux fois le même événement.
    private Task<AppointmentStore>? _opening;

    /// <summary>Ce que la dernière lecture a constaté : accès refusé, calendrier absent, ou lecture réussie.</summary>
    public IntegrationProbe Probe { get; private set; }

    private async Task<AppointmentStore> OpenStoreAsync()
    {
        AppointmentStore store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);

        try
        {
            store.ChangeTracker.Enable();
            store.StoreChanged += OnStoreChanged;
        }
        catch (Exception)
        {
            // Suivi des changements indisponible : la relecture périodique suffit.
        }

        _store = store;
        return store;
    }

    private void OnStoreChanged(AppointmentStore sender, AppointmentStoreChangedEventArgs args) => Changed?.Invoke();

    /// <summary>Cesse d'écouter le calendrier : une fonctionnalité désactivée ne laisse aucun écouteur.</summary>
    public void Stop()
    {
        if (_store is not null)
        {
            _store.StoreChanged -= OnStoreChanged;
        }

        _store = null;
        _opening = null;
    }

    /// <summary>Le calendrier a changé : relire le prochain rendez-vous.</summary>
    public event Action? Changed;

    /// <summary>Le prochain rendez-vous qui n'est pas terminé, dans les douze heures ; null s'il n'y en a pas.</summary>
    public async Task<CalendarMeeting?> NextAsync(DateTimeOffset now)
    {
        // Un constat d'une lecture précédente ne vaut rien pour celle-ci.
        Probe = IntegrationProbe.Unknown;

        try
        {
            _opening ??= OpenStoreAsync();
            AppointmentStore store;

            try
            {
                store = await _opening.ConfigureAwait(false);
            }
            catch (Exception)
            {
                _opening = null;
                throw;
            }

            var options = new FindAppointmentsOptions { MaxCount = 10 };

            foreach (string property in new[]
            {
                AppointmentProperties.Subject, AppointmentProperties.StartTime, AppointmentProperties.Duration,
                AppointmentProperties.Location, AppointmentProperties.Details, AppointmentProperties.OnlineMeetingLink,
                AppointmentProperties.AllDay, AppointmentProperties.IsCanceledMeeting
            })
            {
                options.FetchProperties.Add(property);
            }

            var found = await store.FindAppointmentsAsync(now.AddMinutes(-15), TimeSpan.FromHours(12), options);

            Appointment? next = found
                .Where(a => !a.AllDay && !a.IsCanceledMeeting && a.StartTime + a.Duration > now)
                .OrderBy(a => a.StartTime)
                .FirstOrDefault();

            Probe = IntegrationProbe.Ok;

            return next is null
                ? null
                : new CalendarMeeting(next.LocalId, next.Subject ?? string.Empty, next.StartTime, next.StartTime + next.Duration, next.Location, next.Details, next.OnlineMeetingLink);
        }
        catch (Exception ex)
        {
            // Pas d'identité, capacité refusée, calendrier absent : rien à montrer, mais on le sait.
            Probe = ex is UnauthorizedAccessException ? IntegrationProbe.Denied : IntegrationProbe.Missing;
            return null;
        }
    }
}
