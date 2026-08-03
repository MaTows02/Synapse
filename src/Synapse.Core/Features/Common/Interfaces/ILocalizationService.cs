using System;
using System.Collections.Generic;
using Synapse.Core.Features.Common.Models;

namespace Synapse.Core.Features.Common.Interfaces;

public interface ILocalizationService
{
    string GetString(string key);

    string GetString(string key, params object[] args);

    string CurrentLanguage { get; }

    bool IsRightToLeft { get; }

    bool SetLanguage(string languageCode);

    event EventHandler? LanguageChanged;

    IReadOnlyList<LanguageOption> GetAvailableLanguages();
}
