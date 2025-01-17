﻿using Blazored.LocalStorage;

namespace Tellurian.Trains.MeetingApp.Client.Services
{
    public class RegistrationsService(ILocalStorageService localStorage)
    {
        private readonly ILocalStorageService LocalStorage = localStorage;

        public async Task<bool> SetAsync(Registration registration)
        {
            if (registration is null) return false;
            registration.IsInstructionVisible = false;
            await LocalStorage.SetItemAsync(Registration.Key, registration).ConfigureAwait(false);
            return true;
        }

        public async Task<Registration> GetAsync() => 
            await LocalStorage.GetItemAsync<Registration>(Registration.Key).ConfigureAwait(false) ?? Registration.Default;

        public async Task<Registration> UseAvailableClockOnlyAsync(IEnumerable<string>? availableClocks)
        {
            var registration = await GetAsync();
            if (availableClocks is null || !availableClocks.Contains(registration.ClockName, StringComparer.OrdinalIgnoreCase))
            {
                registration.ClockName = ClockSettings.DemoClockName;
                registration.ClockPassword = ClockSettings.DemoClockPassword;
                await SetAsync(registration);
            }
            return registration;
        }
    }
}