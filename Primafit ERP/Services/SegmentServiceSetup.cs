using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public sealed class SegmentsSetupService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public SegmentsSetupService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // -----------------------------
        // CONFIG (names + active flags)
        // -----------------------------
        public async Task<SegCoaConfig> GetOrCreateConfigAsync(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();

            var cfg = await ctx.Set<SegCoaConfig>().FirstOrDefaultAsync(x => x.CompanyId == companyId);
            if (cfg != null) return cfg;

            cfg = new SegCoaConfig { CompanyId = companyId };
            ctx.Add(cfg);
            await ctx.SaveChangesAsync();
            return cfg;
        }

        public async Task<string?> SaveConfigAsync(SegCoaConfig cfg)
        {
            try
            {
                using var ctx = _dbFactory.CreateDbContext();

                var existing = await ctx.Set<SegCoaConfig>().FirstOrDefaultAsync(x => x.CompanyId == cfg.CompanyId);
                if (existing == null)
                {
                    // force segment0 active by default
                    cfg.Segment1Active = cfg.Segment1Active;
                    ctx.Add(cfg);
                }
                else
                {
                    existing.Segment0Name = Clean(cfg.Segment0Name, "Segment 0");
                    existing.Segment1Name = Clean(cfg.Segment1Name, "Segment 1");
                    existing.Segment2Name = Clean(cfg.Segment2Name, "Segment 2");
                    existing.Segment3Name = Clean(cfg.Segment3Name, "Segment 3");
                    existing.Segment4Name = Clean(cfg.Segment4Name, "Segment 4");
                    existing.Segment5Name = Clean(cfg.Segment5Name, "Segment 5");

                    // Segment0 always active; only allow toggles for 1..5
                    existing.Segment1Active = cfg.Segment1Active;
                    existing.Segment2Active = cfg.Segment2Active;
                    existing.Segment3Active = cfg.Segment3Active;
                    existing.Segment4Active = cfg.Segment4Active;
                    existing.Segment5Active = cfg.Segment5Active;
                }

                await ctx.SaveChangesAsync();
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static string Clean(string? s, string fallback)
        {
            s = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ? fallback : s;
        }

        // -----------------------------
        // SEGMENTS CRUD (generic)
        // -----------------------------
        public async Task<List<T>> GetSegmentsAsync<T>(Guid companyId) where T : class
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Set<T>()
                .AsNoTracking()
                .Where(x => EF.Property<Guid>(x, "CompanyId") == companyId)
                .OrderBy(x => EF.Property<string>(x, "Code"))
                .ToListAsync();
        }

        public async Task<string?> SaveSegmentAsync<T>(T model) where T : class
        {
            try
            {
                using var ctx = _dbFactory.CreateDbContext();

                var id = (Guid)typeof(T).GetProperty("Id")!.GetValue(model)!;
                var companyId = (Guid)typeof(T).GetProperty("CompanyId")!.GetValue(model)!;
                var code = (typeof(T).GetProperty("Code")!.GetValue(model)?.ToString() ?? "").Trim();
                var desc = (typeof(T).GetProperty("Description")!.GetValue(model)?.ToString() ?? "").Trim();

                if (companyId == Guid.Empty) return "CompanyId missing.";
                if (string.IsNullOrWhiteSpace(code)) return "Code is required.";
                if (string.IsNullOrWhiteSpace(desc)) return "Description is required.";

                // uniqueness per company per segment table (even without DB unique index)
                var exists = await ctx.Set<T>()
                    .AnyAsync(x =>
                        EF.Property<Guid>(x, "CompanyId") == companyId &&
                        EF.Property<string>(x, "Code") == code &&
                        EF.Property<Guid>(x, "Id") != id);

                if (exists) return $"Code '{code}' already exists in this segment.";

                if (id == Guid.Empty)
                {
                    typeof(T).GetProperty("Id")!.SetValue(model, Guid.NewGuid());
                    ctx.Add(model);
                }
                else
                {
                    ctx.Update(model);
                }

                await ctx.SaveChangesAsync();
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<string?> DeleteSegmentAsync<T>(Guid id) where T : class
        {
            try
            {
                using var ctx = _dbFactory.CreateDbContext();
                var entity = await ctx.Set<T>().FindAsync(id);
                if (entity == null) return "Record not found.";
                ctx.Remove(entity);
                await ctx.SaveChangesAsync();
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
