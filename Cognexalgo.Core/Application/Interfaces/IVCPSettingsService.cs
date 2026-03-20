using Cognexalgo.Core.Models;

namespace Cognexalgo.Core.Application.Interfaces
{
    /// <summary>
    /// Persists and retrieves user-configurable VCP strategy settings.
    /// The storage mechanism (JSON file, database, registry) is an infrastructure
    /// concern — this interface remains free of any I/O assumptions.
    /// </summary>
    public interface IVCPSettingsService
    {
        /// <summary>
        /// Loads the current VCP settings from the underlying store.
        /// If no settings have been saved yet, returns a <see cref="VCPSettings"/>
        /// instance populated with all default values.
        /// Never returns <c>null</c>.
        /// </summary>
        /// <returns>The persisted or default <see cref="VCPSettings"/>.</returns>
        VCPSettings Load();

        /// <summary>
        /// Persists the supplied <see cref="VCPSettings"/> to the underlying store,
        /// replacing any previously saved values.
        /// </summary>
        /// <param name="settings">
        /// The settings object to save. Must not be <c>null</c>.
        /// </param>
        void Save(VCPSettings settings);
    }
}
