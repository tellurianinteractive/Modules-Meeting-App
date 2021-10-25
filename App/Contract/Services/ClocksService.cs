﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Tellurian.Trains.MeetingApp.Contract.Services
{
    public class ClocksService
    {
        public ClocksService(HttpClient http)
        {
            Http = http;
        }

        private readonly HttpClient Http;

        public async Task<IEnumerable<string>?> AvailableClocks() =>
            await Http.GetFromJsonAsync<IEnumerable<string>>("api/clocks/available").ConfigureAwait(false);

        public async Task<IEnumerable<ClockUser>?> Users(string? clockName, string? administratorPassword)
        {
            if (string.IsNullOrEmpty(clockName)) return Array.Empty<ClockUser>();
            try
            {
                return await Http.GetFromJsonAsync<IEnumerable<ClockUser>>($"api/clocks/{clockName}/users?password={administratorPassword}").ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return Array.Empty<ClockUser>();
            }
        }

        public async Task<ClockStatus?> GetStatus(string clockName, string? userName, string? clientVersion)
        {
            try
            {
                return await Http.GetFromJsonAsync<ClockStatus>($"api/clocks/{clockName}/Time?user={userName}&client={clientVersion}").ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return new ClockStatus { Name = clockName, IsUnavailable = true };
            }
        }

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

        public async Task<HttpResponseMessage> Start(string clockName, string? clockPassword, string? userName) =>
            await Http.PutAsync($"api/clocks/{clockName}/start?user={userName}&password={clockPassword}", null).ConfigureAwait(false);

        public async Task<HttpResponseMessage> Stop(string clockName, string? clockPassword, string? userName, string stopReason) =>
            await Http.PutAsync($"api/clocks/{clockName}/stop?user={userName}&password={clockPassword}&reason={stopReason}", null).ConfigureAwait(false);

        public async Task<HttpResponseMessage> User(string clockName, string? clockPassword, string? userName, string? clientVersionNumber) =>
            await Http.PutAsync($"api/clocks/{clockName}/user?user={userName}&password={clockPassword}&client={clientVersionNumber}", null).ConfigureAwait(false);

        public async Task<HttpResponseMessage> Update(string clockName, string? userName, string? administratorPassword, ClockSettings settings) =>
            await Http.PutAsJsonAsync($"api/clocks/{clockName}/settings?user={userName}&password={administratorPassword}", settings).ConfigureAwait(false);
        public async Task<HttpResponseMessage> Create(string? userName, ClockSettings settings)
        {
            return await Http.PostAsJsonAsync($"api/clocks/create?user={userName}", settings).ConfigureAwait(false);
        }

        public async Task<ClockSettings?> GetSettings(string? clockName, string? administratorPassword)
        {
            try
            {
                var response = await Http.GetAsync($"api/clocks/{clockName}/settings?password={administratorPassword}").ConfigureAwait(false);
                return await response.GetSettings();
            }
            catch (HttpRequestException)
            {
                return null;
            }
        }
    }
    public static class ClockServiceExtensions
    {
        public static async Task<ClockSettings?> GetSettings(this HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ClockSettings?>().ConfigureAwait(false);
            }
            return null;
        }

        public static async Task<string> ErrorMessage(this HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorMessage>().ConfigureAwait(false);
                if (error is not null)
                {
                    return string.Join(", ", error.Messages);
                }
            }
            return string.Empty;
        }
    }
}
