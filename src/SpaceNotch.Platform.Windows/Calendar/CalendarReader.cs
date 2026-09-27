using System;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>Le calendrier a changé : relire le prochain rendez-vous.</summary>
    public event Action? Changed;

    /// <summary>Le prochain rendez-vous qui n'est pas terminé, dans les douze heures ; null s'il n'y en a pas.</summary>
    public async Task<CalendarMeeting?> NextAsync(DateTimeOffset now)
    {
        try
        {
            if (_store is null)
            {
                _store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);

                try
                {
                    _store.ChangeTracker.Enable();
                    _store.StoreChanged += (_, _) => Changed?.Invoke();
                }
                catch (Exception)
                {
                    // Suivi des changements indisponible : la relecture périodique suffit.
                }
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

            var found = await _store.FindAppointmentsAsync(now.AddMinutes(-15), TimeSpan.FromHours(12), options);

            Appointment? next = found
                .Where(a => !a.AllDay && !a.IsCanceledMeeting && a.StartTime + a.Duration > now)
                .OrderBy(a => a.StartTime)
                .FirstOrDefault();

            return next is null
                ? null
                : new CalendarMeeting(next.LocalId, next.Subject ?? string.Empty, next.StartTime, next.StartTime + next.Duration, next.Location, next.Details, next.OnlineMeetingLink);
        }
        catch (Exception)
        {
            // Pas d'identité, capacité refusée, calendrier absent : rien à montrer.
            return null;
        }
    }
}
