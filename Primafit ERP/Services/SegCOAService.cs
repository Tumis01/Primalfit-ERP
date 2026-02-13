using Microsoft.EntityFrameworkCore;
using PrimafitERP.Data;
using Primafit_ERP.Components.Models;

namespace Primafit_ERP.Services
{
    public sealed class SegCoaService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public SegCoaService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
        }

        // -----------------------------
        // CONFIG
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
                    ctx.Add(cfg);
                }
                else
                {
                    existing.Segment0Name = cfg.Segment0Name;
                    existing.Segment1Name = cfg.Segment1Name;
                    existing.Segment2Name = cfg.Segment2Name;
                    existing.Segment3Name = cfg.Segment3Name;
                    existing.Segment4Name = cfg.Segment4Name;
                    existing.Segment5Name = cfg.Segment5Name;

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

        // -----------------------------
        // SEGMENT LISTS (read)
        // -----------------------------
        public Task<List<Segment0>> GetSegment0Async(Guid companyId) => GetSegmentsAsync<Segment0>(companyId);
        public Task<List<Segment1>> GetSegment1Async(Guid companyId) => GetSegmentsAsync<Segment1>(companyId);
        public Task<List<Segment2>> GetSegment2Async(Guid companyId) => GetSegmentsAsync<Segment2>(companyId);
        public Task<List<Segment3>> GetSegment3Async(Guid companyId) => GetSegmentsAsync<Segment3>(companyId);
        public Task<List<Segment4>> GetSegment4Async(Guid companyId) => GetSegmentsAsync<Segment4>(companyId);
        public Task<List<Segment5>> GetSegment5Async(Guid companyId) => GetSegmentsAsync<Segment5>(companyId);

        private async Task<List<T>> GetSegmentsAsync<T>(Guid companyId) where T : class
        {
            using var ctx = _dbFactory.CreateDbContext();

            // Works because all Segment* have CompanyId, Code, Description
            return await ctx.Set<T>()
                .AsNoTracking()
                .OrderBy(x => EF.Property<string>(x, "Code"))
                .ToListAsync();
        }

        // -----------------------------
        // SEGMENT CRUD (add/delete)
        // -----------------------------
        public Task<string?> AddSegment0Async(Guid companyId, string code, string description) => AddSegmentAsync<Segment0>(companyId, code, description);
        public Task<string?> AddSegment1Async(Guid companyId, string code, string description) => AddSegmentAsync<Segment1>(companyId, code, description);
        public Task<string?> AddSegment2Async(Guid companyId, string code, string description) => AddSegmentAsync<Segment2>(companyId, code, description);
        public Task<string?> AddSegment3Async(Guid companyId, string code, string description) => AddSegmentAsync<Segment3>(companyId, code, description);
        public Task<string?> AddSegment4Async(Guid companyId, string code, string description) => AddSegmentAsync<Segment4>(companyId, code, description);
        public Task<string?> AddSegment5Async(Guid companyId, string code, string description) => AddSegmentAsync<Segment5>(companyId, code, description);

        private async Task<string?> AddSegmentAsync<T>(Guid companyId, string code, string description) where T : class, new()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code)) return "Code is required.";
                if (string.IsNullOrWhiteSpace(description)) return "Description is required.";

                using var ctx = _dbFactory.CreateDbContext();

                var entity = new T();
                Set(entity, "Id", Guid.NewGuid());
                Set(entity, "CompanyId", companyId);
                Set(entity, "Code", code.Trim());
                Set(entity, "Description", description.Trim());

                ctx.Add(entity);
                await ctx.SaveChangesAsync();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
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
            catch (Exception ex) { return ex.Message; }
        }

        // -----------------------------
        // SegAccountTypes (fixed list)
        // -----------------------------
        public async Task<List<SegAccountType>> GetAccountTypesAsync()
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Set<SegAccountType>()
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .ToListAsync();
        }

        // -----------------------------
        // COA list + create
        // -----------------------------
        public async Task<List<SegChartOfAccount>> GetCoaAsync(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();

            return await ctx.Set<SegChartOfAccount>()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.AccountCode)
                .ToListAsync();
        }

        public sealed class SegCoaDraft
        {
            public Guid CompanyId { get; set; }

            public Guid Segment0Id { get; set; }
            public Guid? Segment1Id { get; set; }
            public Guid? Segment2Id { get; set; }
            public Guid? Segment3Id { get; set; }
            public Guid? Segment4Id { get; set; }
            public Guid? Segment5Id { get; set; }

            public string Description { get; set; } = "";
            public int SegAccountTypeId { get; set; }
            public bool AllowJournal { get; set; } = true;
        }

        public async Task<(string? error, SegChartOfAccount? created)> CreateCoaAsync(SegCoaDraft draft)
        {
            try
            {
                using var ctx = _dbFactory.CreateDbContext();

                var cfg = await GetOrCreateConfigAsync(draft.CompanyId);

                // 1. Base Requirement: Segment 0 is always mandatory
                if (draft.Segment0Id == Guid.Empty)
                    return ("Segment0 is required.", null);

                // 2. Logic Check: Are we trying to use segmentation?
                // Check if any of the optional segments have a value selected
                bool isSegmented =
                    (draft.Segment1Id != null && draft.Segment1Id != Guid.Empty) ||
                    (draft.Segment2Id != null && draft.Segment2Id != Guid.Empty) ||
                    (draft.Segment3Id != null && draft.Segment3Id != Guid.Empty) ||
                    (draft.Segment4Id != null && draft.Segment4Id != Guid.Empty) ||
                    (draft.Segment5Id != null && draft.Segment5Id != Guid.Empty);

                // 3. Validation Rules
                if (isSegmented)
                {
                    // Rule: If using segments, ALL ACTIVE segments must have a value.
                    // You cannot have "Segment 0 + Segment 1" if Segment 2 is also active.

                    if (cfg.Segment1Active && (draft.Segment1Id == null || draft.Segment1Id == Guid.Empty))
                        return ($"{cfg.Segment1Name} is active and must be selected for a segmented account.", null);

                    if (cfg.Segment2Active && (draft.Segment2Id == null || draft.Segment2Id == Guid.Empty))
                        return ($"{cfg.Segment2Name} is active and must be selected for a segmented account.", null);

                    if (cfg.Segment3Active && (draft.Segment3Id == null || draft.Segment3Id == Guid.Empty))
                        return ($"{cfg.Segment3Name} is active and must be selected for a segmented account.", null);

                    if (cfg.Segment4Active && (draft.Segment4Id == null || draft.Segment4Id == Guid.Empty))
                        return ($"{cfg.Segment4Name} is active and must be selected for a segmented account.", null);

                    if (cfg.Segment5Active && (draft.Segment5Id == null || draft.Segment5Id == Guid.Empty))
                        return ($"{cfg.Segment5Name} is active and must be selected for a segmented account.", null);
                }
                else
                {
                    // Rule: Pure Segment 0 account. 
                    // This is allowed per requirements. We proceed without checking active flags for 1-5.
                }

                // Compute code + default description from selected segment rows
                var (accountCode, computedDesc, err) = await BuildCodeAndDescriptionAsync(ctx, draft.CompanyId,
                    draft.Segment0Id, draft.Segment1Id, draft.Segment2Id, draft.Segment3Id, draft.Segment4Id, draft.Segment5Id);

                if (!string.IsNullOrEmpty(err)) return (err, null);

                var finalDesc = string.IsNullOrWhiteSpace(draft.Description) ? computedDesc : draft.Description.Trim();
                if (string.IsNullOrWhiteSpace(finalDesc)) return ("Description is required.", null);
                if (draft.SegAccountTypeId <= 0) return ("Account type is required.", null);

                // Ensure unique account code per company
                var exists = await ctx.Set<SegChartOfAccount>()
                    .AnyAsync(x => x.CompanyId == draft.CompanyId && x.AccountCode == accountCode);

                if (exists) return ($"COA account code already exists: {accountCode}", null);

                var coa = new SegChartOfAccount
                {
                    CompanyId = draft.CompanyId,
                    Segment0Id = draft.Segment0Id,
                    Segment1Id = draft.Segment1Id,
                    Segment2Id = draft.Segment2Id,
                    Segment3Id = draft.Segment3Id,
                    Segment4Id = draft.Segment4Id,
                    Segment5Id = draft.Segment5Id,
                    AccountCode = accountCode,
                    Description = finalDesc,
                    SegAccountTypeId = draft.SegAccountTypeId,
                    AllowJournal = draft.AllowJournal
                };

                ctx.Add(coa);
                await ctx.SaveChangesAsync();
                return (null, coa);
            }
            catch (Exception ex)
            {
                return (ex.Message, null);
            }
        }

        private async Task<(string code, string desc, string? error)> BuildCodeAndDescriptionAsync(
            AppDbContext ctx,
            Guid companyId,
            Guid seg0Id,
            Guid? seg1Id,
            Guid? seg2Id,
            Guid? seg3Id,
            Guid? seg4Id,
            Guid? seg5Id)
        {
            // fetch all selected segments (must belong to company)
            var s0 = await ctx.Set<Segment0>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == seg0Id && x.CompanyId == companyId);
            if (s0 == null) return ("", "", "Invalid Segment0 selection.");

            Segment1? s1 = null; Segment2? s2 = null; Segment3? s3 = null; Segment4? s4 = null; Segment5? s5 = null;

            if (seg1Id != null && seg1Id != Guid.Empty) s1 = await ctx.Set<Segment1>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == seg1Id && x.CompanyId == companyId);
            if (seg2Id != null && seg2Id != Guid.Empty) s2 = await ctx.Set<Segment2>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == seg2Id && x.CompanyId == companyId);
            if (seg3Id != null && seg3Id != Guid.Empty) s3 = await ctx.Set<Segment3>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == seg3Id && x.CompanyId == companyId);
            if (seg4Id != null && seg4Id != Guid.Empty) s4 = await ctx.Set<Segment4>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == seg4Id && x.CompanyId == companyId);
            if (seg5Id != null && seg5Id != Guid.Empty) s5 = await ctx.Set<Segment5>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == seg5Id && x.CompanyId == companyId);

            // if ID provided but not found => error
            if (seg1Id != null && seg1Id != Guid.Empty && s1 == null) return ("", "", "Invalid Segment1 selection.");
            if (seg2Id != null && seg2Id != Guid.Empty && s2 == null) return ("", "", "Invalid Segment2 selection.");
            if (seg3Id != null && seg3Id != Guid.Empty && s3 == null) return ("", "", "Invalid Segment3 selection.");
            if (seg4Id != null && seg4Id != Guid.Empty && s4 == null) return ("", "", "Invalid Segment4 selection.");
            if (seg5Id != null && seg5Id != Guid.Empty && s5 == null) return ("", "", "Invalid Segment5 selection.");

            var codes = new List<string> { s0.Code };
            var descs = new List<string> { s0.Description };

            if (s1 != null) { codes.Add(s1.Code); descs.Add(s1.Description); }
            if (s2 != null) { codes.Add(s2.Code); descs.Add(s2.Description); }
            if (s3 != null) { codes.Add(s3.Code); descs.Add(s3.Description); }
            if (s4 != null) { codes.Add(s4.Code); descs.Add(s4.Description); }
            if (s5 != null) { codes.Add(s5.Code); descs.Add(s5.Description); }

            var code = string.Join("/", codes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
            var desc = string.Join(" - ", descs.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));

            if (string.IsNullOrWhiteSpace(code)) return ("", "", "AccountCode could not be computed.");
            return (code, desc, null);
        }

        // -----------------------------
        // small reflection setter (for segment generic insert)
        // -----------------------------
        private static void Set(object obj, string prop, object value)
        {
            var p = obj.GetType().GetProperty(prop);
            if (p == null) throw new InvalidOperationException($"Property {prop} not found on {obj.GetType().Name}");
            p.SetValue(obj, value);
        }
    }
}