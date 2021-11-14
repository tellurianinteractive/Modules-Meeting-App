﻿using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Timers;
using Tellurian.Trains.MeetingApp.Contract.Model;
using Timer = System.Timers.Timer;

[assembly: InternalsVisibleTo("Tellurian.Trains.MeetingApp.Clocks.Tests")]

namespace Tellurian.Trains.MeetingApp.Clocks.Implementations;

public sealed class ClockServer : IDisposable, IClock
{
    private readonly ClockServerOptions Options;
    private readonly Timer ClockTimer;
    private readonly IList<User> Clients = new List<User>();
    public event EventHandler<string>? OnUpdate;

    public static Version? ServerVersion => Assembly.GetAssembly(typeof(ClockServer))?.GetName().Version;

    public ClockServer(IOptions<ClockServerOptions> options)
    {
        Options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        Name = Options.Name;
        AdministratorPassword = Options.Password;
        UserPassword = string.Empty;
        UpdateSettings(Options.AsSettings());
        Elapsed = TimeSpan.Zero;
        ClockTimer = new Timer(1000);
        ClockTimer.Elapsed += Tick;
        UtcOffset = TimeZoneInfo.FindSystemTimeZoneById(Options.TimeZoneId).GetUtcOffset(DateTime.Today);
    }
    public string Name { get; internal set; }
    public TimeSpan UtcOffset { get; }
    public Settings Settings { get => this.AsSettings(); set => UpdateSettings(value); }
    public Status Status => this.AsStatus();
    public IEnumerable<User> ClockUsers => Clients.ToArray();
    public DateTimeOffset LastAccessedTime => Clients.Count > 0 ? Clients.Max(c => c.LastUsedTime) : DateTimeOffset.Now;

    public bool IsUser(string? password) =>
        string.IsNullOrWhiteSpace(UserPassword) && string.IsNullOrWhiteSpace(password) ||
        IsAdministrator(password) ||
        !string.IsNullOrWhiteSpace(password) && password.Equals(UserPassword, StringComparison.OrdinalIgnoreCase);

    private bool IsStoppingUser(string? userName, string? password) =>
        IsUser(password) &&
        !string.IsNullOrWhiteSpace(userName) && userName.Equals(StoppingUser, StringComparison.OrdinalIgnoreCase);

    public bool IsAdministrator(string? password) =>
        !string.IsNullOrWhiteSpace(password) && password.Equals(AdministratorPassword, StringComparison.OrdinalIgnoreCase);

    internal bool IsPaused { get; private set; }
    internal bool IsRunning { get; private set; }
    internal bool IsRealtime { get; private set; }
    internal bool IsCompleted => Elapsed >= Duration;
    internal Message Message { get; private set; } = new Message();
    internal bool ShowRealTimeWhenPaused { get; private set; }
    internal double Speed { get; private set; }
    internal PauseReason PauseReason { get; private set; }
    internal StopReason StopReason { get; private set; }
    internal string? StoppingUser { get; private set; }
    internal Weekday Weekday => IsRealtime ? (Weekday)RealDayAndTime.WeekdayNumber() : (Weekday)FastTime.WeekdayNumber();
    internal string AdministratorPassword { get; set; }
    internal string UserPassword { get; private set; }

    internal TimeSpan StartDayAndTime { get; private set; }
    internal TimeSpan StartTime => StartDayAndTime - TimeSpan.FromDays(StartDayAndTime.Days);
    internal TimeSpan Duration { get; private set; }
    internal TimeSpan Elapsed { get; private set; }
    internal TimeSpan FastTime => StartDayAndTime + Elapsed;
    internal TimeSpan Time { get { return IsRealtime ? RealDayAndTime : FastTime; } }
    internal TimeSpan RealEndTime => RealDayAndTime + TimeSpan.FromHours((Duration - Elapsed).TotalHours / Speed) + PauseDuration;
    internal TimeSpan FastEndTime => StartDayAndTime + Duration;
    internal TimeSpan? PauseTime { get; private set; }
    internal TimeSpan? ExpectedResumeTime { get; private set; }
    internal TimeSpan PauseDuration => ExpectedResumeTime.HasValue && PauseTime.HasValue ? ExpectedResumeTime.Value - PauseTime.Value : TimeSpan.Zero;
    internal TimeSpan RealDayAndTime { get { var now = DateTime.UtcNow + UtcOffset; var day = (int)now.DayOfWeek; return new TimeSpan(day == 0 ? 7 : day, now.Hour, now.Minute, now.Second); } }
    internal TimeSpan RealTime => RealDayAndTime - TimeSpan.FromDays(RealDayAndTime.Days);
    public override string ToString() => Name;

    #region Clock control
    public ClockServer StartServer(Settings settings)
    {
        UpdateSettings(settings);
        return this;
    }

    public void StopServer()
    {
        ClockTimer.Stop();
    }

    public bool TryStartTick(string? user, string? password)
    {
        if (IsRunning) return true;
        if (IsStoppingUser(user, password) || IsAdministrator(password))
        {
            if (IsAdministrator(password)) ResetPause();
            ResetStopping();
            ClockTimer.Start();
            IsRunning = true;
            if (Options.Sounds.PlayAnnouncements) PlaySound(Options.Sounds.StartSoundFilePath);
            return true;
        }
        return false;
    }

    public bool TryStopTick(string? user, string? password, StopReason reason)
    {
        if (IsRunning && IsUser(password) && !string.IsNullOrWhiteSpace(user))
        {
            StopTick(reason, user);
            return true;
        }
        return false;
    }

    public void StopTick(StopReason reason, string user)
    {
        if (!IsRunning) return;
        StopReason = reason;
        StoppingUser = user;
        StopTick();
    }

    public void StopTick()
    {
        if (!IsRunning) return;
        ClockTimer.Stop();
        IsRunning = false;
        if (Options.Sounds.PlayAnnouncements) PlaySound(Options.Sounds.StopSoundFilePath);
    }

    #endregion Clock control

    private void Tick(object? me, ElapsedEventArgs args)
    {
        IncreaseTime();
        if (PauseTime.HasValue && RealTime >= PauseTime.Value)
        {
            IsPaused = true;
            IsRealtime = ShowRealTimeWhenPaused;
            StopTick();
        }
        if (IsCompleted)
            StopTick();
    }

    private void IncreaseTime()
    {
        var previousElapsed = Elapsed;
        Elapsed = Elapsed.Add(TimeSpan.FromSeconds(Speed));
        if (Elapsed.Minutes != previousElapsed.Minutes)
            if (OnUpdate is not null) OnUpdate(this, Name);
    }
    public bool UpdateSettings(IPAddress? ipAddress, string? userName, string? password, Settings settings)
    {
        if (!IsAdministrator(password)) return false;

        UpdateUser(ipAddress, userName);
        RemoveInactiveUsers(TimeSpan.FromMinutes(30));
        var result = UpdateSettings(settings);
        if (OnUpdate is not null) OnUpdate(this, Name);
        return result;
    }

    private bool UpdateSettings(Settings settings)
    {
        if (settings == null) return false;
        if (Name?.Equals(settings.Name, StringComparison.OrdinalIgnoreCase) != true) return false;
        if (!string.IsNullOrWhiteSpace(settings.AdministratorPassword)) AdministratorPassword = settings.AdministratorPassword;
        if (!string.IsNullOrWhiteSpace(settings.UserPassword)) UserPassword = settings.UserPassword;
        IsRealtime = settings.IsRealtime;
        StartDayAndTime = SetStartDayAndTime(settings.StartTime, settings.StartWeekday);
        Speed = settings.Speed ?? Speed;
        Duration = settings.Duration ?? Duration;
        PauseTime = settings.PauseTime;
        PauseReason = settings.PauseReason;
        ExpectedResumeTime = settings.ExpectedResumeTime;
        ShowRealTimeWhenPaused = settings.ShowRealTimeWhenPaused;
        Elapsed = settings.OverriddenElapsedTime.HasValue ? settings.OverriddenElapsedTime.Value - StartTime : Elapsed;
        Message = settings.Message ?? Message;
        if (settings.ShouldRestart) { Elapsed = TimeSpan.Zero; IsRunning = false; }
        if (settings.IsRunning) TryStartTick(StoppingUser, AdministratorPassword); else { StopTick(); }

        TimeSpan SetStartDayAndTime(TimeSpan? startTime, Weekday? startDay) =>
            new((int)(startDay ?? Weekday.NoDay), startTime?.Hours ?? Options.StartTime.Hours, startTime?.Minutes ?? Options.StartTime.Minutes, 0);
        return true;
    }

    public bool UpdateUser(IPAddress? ipAddress, string? userName, string? clientVersion = "")
    {
        const string Unknown = "Unknown";
        if (string.IsNullOrWhiteSpace(userName)) userName = Unknown;
        lock (Clients)
        {
            if (ipAddress is null) return false;
            if (HasSameUserNameWithOtherIpAddress(ipAddress, userName)) return false;
            var existing = Clients.Where(c => c.IPAddress.Equals(ipAddress)).ToArray();
            if (existing.Length == 0)
                Clients.Add(new User(ipAddress, userName, clientVersion));
            else
            {
                var unknown = existing.Where(e => Unknown.Equals(e.UserName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (unknown.Length == 1) unknown[0].Update(userName, clientVersion);
                else
                {
                    var named = existing.Where(e => e.UserName?.Equals(userName, StringComparison.OrdinalIgnoreCase) == true).ToArray();
                    if (named.Length == 1) named[0].Update(userName, clientVersion);
                }
            }
            return true;
        }
    }

    public void RemoveInactiveUsers(TimeSpan age)
    {
        lock (Clients)
        {
            foreach (var user in Clients.Where(c => c.LastUsedTime + age < DateTimeOffset.Now).ToList()) Clients.Remove(user);
        }
    }

    private bool HasSameUserNameWithOtherIpAddress(IPAddress? ipAddress, string? userName) =>
        Clients.Any(c => !c.IPAddress.Equals(ipAddress) && !string.IsNullOrWhiteSpace(c.UserName) && c.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase));

    private void ResetPause()
    {
        if (IsPaused)
        {
            IsPaused = false;
            PauseTime = null;
            ExpectedResumeTime = null;
            PauseReason = PauseReason.NoReason;
        }
    }

    private void ResetStopping()
    {
        StoppingUser = string.Empty;
        StopReason = StopReason.SelectStopReason;
    }

    public static void PlaySound(string? soundFilePath)
    {
        if (string.IsNullOrEmpty(soundFilePath)) return;
        if (File.Exists(soundFilePath)) Process.Start("powershell", $"-c (New-Object Media.SoundPlayer '{soundFilePath}').PlaySync();");
    }

    public static IPAddress GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        var ip = host.AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork && a.ToString().StartsWith("192.", StringComparison.OrdinalIgnoreCase));
        return ip.Any() ? ip.First() : IPAddress.Loopback;
    }


    #region IDisposable Support
    private bool disposedValue; // To detect redundant calls

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                ClockTimer?.Dispose();
            }
            disposedValue = true;
        }
    }
    // This code added to correctly implement the disposable pattern.
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(true);
    }
    #endregion
}