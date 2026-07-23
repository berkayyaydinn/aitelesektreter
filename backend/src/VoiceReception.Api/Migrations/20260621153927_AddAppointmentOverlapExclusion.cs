using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceReception.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentOverlapExclusion : Migration
    {
        // Yalnız Postgres'te uygulanır. SQLite lokal/test EnsureCreated migration'ları atlar zaten;
        // ActiveProvider guard'ı başka bir sağlayıcıyla yanlışlıkla çalıştırmaya karşı ek koruma.
        private const string Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != Npgsql) return;

            // Eşzamanlı (TOCTOU) çifte-rezervasyonu DB seviyesinde atomik engelle:
            // aynı tenant + örtüşen [StartUtc, EndUtc) aralığı yalnız Booked (Status=0) randevularda yasak.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(@"
                ALTER TABLE ""Appointments"" ADD CONSTRAINT ""appt_no_overlap""
                EXCLUDE USING gist (
                    ""TenantId"" WITH =,
                    tstzrange(""StartUtc"", ""EndUtc"") WITH &&
                ) WHERE (""Status"" = 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != Npgsql) return;

            migrationBuilder.Sql(@"ALTER TABLE ""Appointments"" DROP CONSTRAINT IF EXISTS ""appt_no_overlap"";");
        }
    }
}
