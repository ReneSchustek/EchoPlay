using System;
using EchoPlay.Data.Entities.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EchoPlay.Data.Configurations
{
    /// <summary>
    /// EF-Core-Konfiguration für die SecureSettings-Tabelle.
    /// </summary>
    public sealed class SecureSettingConfiguration : IEntityTypeConfiguration<SecureSetting>
    {
        /// <inheritdoc/>
        public void Configure(EntityTypeBuilder<SecureSetting> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            _ = builder.ToTable("SecureSettings");
            _ = builder.HasKey(s => s.Id);

            _ = builder.Property(s => s.Key)
                   .IsRequired()
                   .HasMaxLength(64);

            // UNIQUE schließt Soft-Delete-Einträge aus, damit nach DeleteAsync ein Re-Insert für denselben Key möglich bleibt.
            _ = builder.HasIndex(s => s.Key)
                   .IsUnique()
                   .HasFilter("IsDeleted = 0");
        }
    }
}
