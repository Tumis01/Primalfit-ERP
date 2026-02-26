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
                    // Copy config values
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
            catch (Exception ex) { return ex.Message; }
        }

        // -----------------------------
        // SEGMENT LISTS
        // -----------------------------
        public async Task<List<Segment0>> GetSegment0Async(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Segment0s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment1>> GetSegment1Async(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Segment1s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment2>> GetSegment2Async(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Segment2s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment3>> GetSegment3Async(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Segment3s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment4>> GetSegment4Async(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Segment4s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment5>> GetSegment5Async(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Segment5s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        private async Task<List<T>> GetSegmentsAsync<T>(Guid companyId) where T : class
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Set<T>()
                .AsNoTracking()
                .OrderBy(x => EF.Property<string>(x, "Code"))
                .ToListAsync();
        }

        // -----------------------------
        // SEGMENT CRUD
        // -----------------------------
        public Task<string?> AddSegment0Async(Guid cId, string c, string d) => AddSegmentAsync<Segment0>(cId, c, d);
        public Task<string?> AddSegment1Async(Guid cId, string c, string d) => AddSegmentAsync<Segment1>(cId, c, d);
        public Task<string?> AddSegment2Async(Guid cId, string c, string d) => AddSegmentAsync<Segment2>(cId, c, d);
        public Task<string?> AddSegment3Async(Guid cId, string c, string d) => AddSegmentAsync<Segment3>(cId, c, d);
        public Task<string?> AddSegment4Async(Guid cId, string c, string d) => AddSegmentAsync<Segment4>(cId, c, d);
        public Task<string?> AddSegment5Async(Guid cId, string c, string d) => AddSegmentAsync<Segment5>(cId, c, d);

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
        // COA & TYPES
        // -----------------------------
        public async Task<List<SegAccountType>> GetAccountTypesAsync()
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Set<SegAccountType>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        }

        public async Task<List<SegChartOfAccount>> GetCoaAsync(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();
            return await ctx.Set<SegChartOfAccount>()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.AccountCode)
                .ToListAsync();
        }

        // -----------------------------
        // SAVE ACCOUNT (Create/Update)
        // -----------------------------
        public sealed class SegCoaDraft
        {
            public Guid? Id { get; set; } // Null = Create, Value = Update
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
            public bool IsActive { get; set; } = true;
        }

        public async Task<(string? error, SegChartOfAccount? result)> SaveAccountAsync(SegCoaDraft draft)
        {
            try
            {
                using var ctx = _dbFactory.CreateDbContext();
                var cfg = await GetOrCreateConfigAsync(draft.CompanyId);

                // 1. Base Requirements
                if (draft.Segment0Id == Guid.Empty) return ("Segment0 is required.", null);
                if (draft.SegAccountTypeId <= 0) return ("Account type is required.", null);

                // 2. BULLETPROOF "All-or-Nothing" Validation
                // Check which segments actually have a valid selected value
                bool hasS1 = draft.Segment1Id.HasValue && draft.Segment1Id.Value != Guid.Empty;
                bool hasS2 = draft.Segment2Id.HasValue && draft.Segment2Id.Value != Guid.Empty;
                bool hasS3 = draft.Segment3Id.HasValue && draft.Segment3Id.Value != Guid.Empty;
                bool hasS4 = draft.Segment4Id.HasValue && draft.Segment4Id.Value != Guid.Empty;
                bool hasS5 = draft.Segment5Id.HasValue && draft.Segment5Id.Value != Guid.Empty;

                // Count how many segments are globally configured as Active
                int activeCount = 0;
                if (cfg.Segment1Active) activeCount++;
                if (cfg.Segment2Active) activeCount++;
                if (cfg.Segment3Active) activeCount++;
                if (cfg.Segment4Active) activeCount++;
                if (cfg.Segment5Active) activeCount++;

                // Count how many of those active segments the user actually filled out
                int selectedCount = 0;
                if (cfg.Segment1Active && hasS1) selectedCount++;
                if (cfg.Segment2Active && hasS2) selectedCount++;
                if (cfg.Segment3Active && hasS3) selectedCount++;
                if (cfg.Segment4Active && hasS4) selectedCount++;
                if (cfg.Segment5Active && hasS5) selectedCount++;

                // The Core Rule: If they started picking sub-segments, they must pick ALL of them.
                if (selectedCount > 0 && selectedCount < activeCount)
                {
                    return ($"select a value for all {activeCount} active segments.", null);
                }

                // 3. Compute Code & Description
                var (accountCode, computedDesc, err) = await BuildCodeAndDescriptionAsync(ctx, draft.CompanyId,
                    draft.Segment0Id, draft.Segment1Id, draft.Segment2Id, draft.Segment3Id, draft.Segment4Id, draft.Segment5Id);

                if (!string.IsNullOrEmpty(err)) return (err, null);

                var finalDesc = string.IsNullOrWhiteSpace(draft.Description) ? computedDesc : draft.Description.Trim();
                if (string.IsNullOrWhiteSpace(finalDesc)) return ("Description is required.", null);

                // 4. Duplicate Check (Exclude self if updating)
                var codeExistsQuery = ctx.Set<SegChartOfAccount>()
                    .Where(x => x.CompanyId == draft.CompanyId && x.AccountCode == accountCode);

                if (draft.Id.HasValue)
                {
                    codeExistsQuery = codeExistsQuery.Where(x => x.Id != draft.Id.Value);
                }

                if (await codeExistsQuery.AnyAsync())
                {
                    return ($"Account code '{accountCode}' already exists.", null);
                }

                SegChartOfAccount account;

                if (draft.Id.HasValue)
                {
                    // --- UPDATE ---
                    account = await ctx.Set<SegChartOfAccount>().FindAsync(draft.Id.Value);
                    if (account == null) return ("Account not found for update.", null);

                    // Update Fields (we safely assign null if they cleared the optional segments)
                    account.Segment0Id = draft.Segment0Id;
                    account.Segment1Id = hasS1 ? draft.Segment1Id : null;
                    account.Segment2Id = hasS2 ? draft.Segment2Id : null;
                    account.Segment3Id = hasS3 ? draft.Segment3Id : null;
                    account.Segment4Id = hasS4 ? draft.Segment4Id : null;
                    account.Segment5Id = hasS5 ? draft.Segment5Id : null;

                    account.AccountCode = accountCode;
                    account.Description = finalDesc;
                    account.SegAccountTypeId = draft.SegAccountTypeId;
                    account.AllowJournal = draft.AllowJournal;
                    account.IsActive = draft.IsActive;

                    ctx.Update(account);
                }
                else
                {
                    // --- CREATE ---
                    account = new SegChartOfAccount
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = draft.CompanyId,
                        Segment0Id = draft.Segment0Id,
                        Segment1Id = hasS1 ? draft.Segment1Id : null,
                        Segment2Id = hasS2 ? draft.Segment2Id : null,
                        Segment3Id = hasS3 ? draft.Segment3Id : null,
                        Segment4Id = hasS4 ? draft.Segment4Id : null,
                        Segment5Id = hasS5 ? draft.Segment5Id : null,
                        AccountCode = accountCode,
                        Description = finalDesc,
                        SegAccountTypeId = draft.SegAccountTypeId,
                        AllowJournal = draft.AllowJournal,
                        IsActive = draft.IsActive
                    };
                    ctx.Add(account);
                }

                await ctx.SaveChangesAsync();
                return (null, account);
            }
            catch (Exception ex)
            {
                return (ex.Message, null);
            }
        }

        public async Task ToggleActiveStatusAsync(Guid accountId)
        {
            using var ctx = _dbFactory.CreateDbContext();
            var acc = await ctx.Set<SegChartOfAccount>().FindAsync(accountId);
            if (acc != null)
            {
                acc.IsActive = !acc.IsActive;
                await ctx.SaveChangesAsync();
            }
        }
        public async Task<List<SegChartOfAccount>> GetActiveCoaAsync(Guid companyId)
        {
            using var ctx = _dbFactory.CreateDbContext();

            return await ctx.Set<SegChartOfAccount>()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.AccountCode)
                .ToListAsync();
        }
        private async Task<(string code, string desc, string? error)> BuildCodeAndDescriptionAsync(
            AppDbContext ctx, Guid companyId, Guid seg0Id, Guid? seg1Id, Guid? seg2Id, Guid? seg3Id, Guid? seg4Id, Guid? seg5Id)
        {
            var s0 = await ctx.Set<Segment0>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == seg0Id && x.CompanyId == companyId);
            if (s0 == null) return ("", "", "Invalid Segment0 selection.");

            // Helper to fetch optional segments safely
            async Task<(string c, string d)?> Fetch<T>(Guid? id) where T : class
            {
                if (id == null || id == Guid.Empty) return null;
                var s = await ctx.Set<T>().AsNoTracking().FirstOrDefaultAsync(x => EF.Property<Guid>(x, "Id") == id && EF.Property<Guid>(x, "CompanyId") == companyId);
                if (s == null) return null;
                return ((string)s.GetType().GetProperty("Code")!.GetValue(s)!, (string)s.GetType().GetProperty("Description")!.GetValue(s)!);
            }

            var s1 = await Fetch<Segment1>(seg1Id);
            var s2 = await Fetch<Segment2>(seg2Id);
            var s3 = await Fetch<Segment3>(seg3Id);
            var s4 = await Fetch<Segment4>(seg4Id);
            var s5 = await Fetch<Segment5>(seg5Id);

            // Validation: if ID passed but not found
            if (seg1Id != null && seg1Id != Guid.Empty && s1 == null) return ("", "", "Invalid Segment1.");
            if (seg2Id != null && seg2Id != Guid.Empty && s2 == null) return ("", "", "Invalid Segment2.");
            if (seg3Id != null && seg3Id != Guid.Empty && s3 == null) return ("", "", "Invalid Segment3.");
            if (seg4Id != null && seg4Id != Guid.Empty && s4 == null) return ("", "", "Invalid Segment4.");
            if (seg5Id != null && seg5Id != Guid.Empty && s5 == null) return ("", "", "Invalid Segment5.");

            var codes = new List<string> { s0.Code };
            var descs = new List<string> { s0.Description };

            if (s1 != null) { codes.Add(s1.Value.c); descs.Add(s1.Value.d); }
            if (s2 != null) { codes.Add(s2.Value.c); descs.Add(s2.Value.d); }
            if (s3 != null) { codes.Add(s3.Value.c); descs.Add(s3.Value.d); }
            if (s4 != null) { codes.Add(s4.Value.c); descs.Add(s4.Value.d); }
            if (s5 != null) { codes.Add(s5.Value.c); descs.Add(s5.Value.d); }

            return (string.Join("/", codes), string.Join(" - ", descs), null);
        }

        private static void Set(object obj, string prop, object value)
        {
            var p = obj.GetType().GetProperty(prop);
            if (p != null) p.SetValue(obj, value);
        }

        public async Task<List<SegChartOfAccount>> GetSegmentedChartOfAccountsAsync(Guid companyId, bool allowJournalOnly = true)
        {
            using var context = _dbFactory.CreateDbContext();

            var q = context.SegChartOfAccounts
                .AsNoTracking()
                .Where(c => c.CompanyId == companyId);

            if (allowJournalOnly)
                q = q.Where(c => c.AllowJournal);

            return await q
                .OrderBy(c => c.AccountCode)
                .ToListAsync();
        }
    }
}