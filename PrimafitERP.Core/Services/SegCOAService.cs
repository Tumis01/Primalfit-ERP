using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System.Text;

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
            using var ctx = await _dbFactory.CreateDbContextAsync();
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
                using var ctx = await _dbFactory.CreateDbContextAsync();
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
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Segment0s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment1>> GetSegment1Async(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Segment1s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment2>> GetSegment2Async(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Segment2s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment3>> GetSegment3Async(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Segment3s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment4>> GetSegment4Async(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Segment4s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<List<Segment5>> GetSegment5Async(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Segment5s
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        private async Task<List<T>> GetSegmentsAsync<T>(Guid companyId) where T : class
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
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

                using var ctx = await _dbFactory.CreateDbContextAsync();
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
                using var ctx = await _dbFactory.CreateDbContextAsync();
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
            using var ctx = await _dbFactory.CreateDbContextAsync();
            return await ctx.Set<SegAccountType>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        }

        public async Task<List<SegChartOfAccount>> GetCoaAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
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
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var cfg = await GetOrCreateConfigAsync(draft.CompanyId);

                // 1. Base Requirements Validation
                if (draft.Segment0Id == Guid.Empty) return ("Segment0 is required.", null);
                if (draft.SegAccountTypeId <= 0) return ("Account type is required.", null);

                // 2. All-or-Nothing Segment Selection Validation
                bool hasS1 = draft.Segment1Id.HasValue && draft.Segment1Id.Value != Guid.Empty;
                bool hasS2 = draft.Segment2Id.HasValue && draft.Segment2Id.Value != Guid.Empty;
                bool hasS3 = draft.Segment3Id.HasValue && draft.Segment3Id.Value != Guid.Empty;
                bool hasS4 = draft.Segment4Id.HasValue && draft.Segment4Id.Value != Guid.Empty;
                bool hasS5 = draft.Segment5Id.HasValue && draft.Segment5Id.Value != Guid.Empty;

                int activeCount = 0;
                if (cfg.Segment1Active) activeCount++;
                if (cfg.Segment2Active) activeCount++;
                if (cfg.Segment3Active) activeCount++;
                if (cfg.Segment4Active) activeCount++;
                if (cfg.Segment5Active) activeCount++;

                int selectedCount = 0;
                if (cfg.Segment1Active && hasS1) selectedCount++;
                if (cfg.Segment2Active && hasS2) selectedCount++;
                if (cfg.Segment3Active && hasS3) selectedCount++;
                if (cfg.Segment4Active && hasS4) selectedCount++;
                if (cfg.Segment5Active && hasS5) selectedCount++;

                if (selectedCount > 0 && selectedCount < activeCount)
                {
                    return ($"Select a value for all {activeCount} active segments.", null);
                }

                // 3. Compute Code & Description
                var (accountCode, computedDesc, err) = await BuildCodeAndDescriptionAsync(ctx, draft.CompanyId,
                    draft.Segment0Id, draft.Segment1Id, draft.Segment2Id, draft.Segment3Id, draft.Segment4Id, draft.Segment5Id);

                if (!string.IsNullOrEmpty(err)) return (err, null);

                var finalDesc = string.IsNullOrWhiteSpace(draft.Description) ? computedDesc : draft.Description.Trim();
                if (string.IsNullOrWhiteSpace(finalDesc)) return ("Description is required.", null);

                // 4. Duplicate Check
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

                // FALLBACK LOGIC: Automatically determine control account constraints (IDs: 24, 25, 26)
                bool isControlAccount = draft.SegAccountTypeId == 24 || draft.SegAccountTypeId == 25 || draft.SegAccountTypeId == 26;
                bool enforcedAllowJournal = isControlAccount ? false : draft.AllowJournal;

                SegChartOfAccount account;

                if (draft.Id.HasValue)
                {
                    // --- UPDATE EXISTING ---
                    account = await ctx.Set<SegChartOfAccount>().FindAsync(draft.Id.Value);
                    if (account == null) return ("Account not found for update.", null);

                    account.Segment0Id = draft.Segment0Id;
                    account.Segment1Id = hasS1 ? draft.Segment1Id : null;
                    account.Segment2Id = hasS2 ? draft.Segment2Id : null;
                    account.Segment3Id = hasS3 ? draft.Segment3Id : null;
                    account.Segment4Id = hasS4 ? draft.Segment4Id : null;
                    account.Segment5Id = hasS5 ? draft.Segment5Id : null;

                    account.AccountCode = accountCode;
                    account.Description = finalDesc;
                    account.SegAccountTypeId = draft.SegAccountTypeId;
                    account.AllowJournal = enforcedAllowJournal; // Force update mapping parameters
                    account.IsActive = draft.IsActive;

                    ctx.Update(account);
                }
                else
                {
                    // --- CREATE NEW ---
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
                        AllowJournal = enforcedAllowJournal, // Force creation mapping parameters
                        IsActive = true
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
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var acc = await ctx.Set<SegChartOfAccount>().FindAsync(accountId);
            if (acc != null)
            {
                acc.IsActive = !acc.IsActive;
                await ctx.SaveChangesAsync();
            }
        }
        public async Task<List<SegChartOfAccount>> GetActiveCoaAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

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
        public async Task<string?> DeleteAccountAsync(Guid accountId)
        {
            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();

                // 1. Check for Posted Transactions
                bool hasPostedTransactions = await ctx.GLTransactions
                    .AnyAsync(t => t.SegCoaId == accountId);

                if (hasPostedTransactions)
                {
                    return "Cannot delete this account because it has posted transactions tied to it. Consider deactivating it instead to hide it from future use.";
                }

                // 2. Check for Unposted Drafts (Journal Lines)
                bool hasUnpostedJournals = await ctx.Set<GLJournalLine>()
                    .AnyAsync(l => l.SegCoaId == accountId);

                if (hasUnpostedJournals)
                {
                    return "Cannot delete this account because it is currently being used in an unposted Draft Journal Entry. Delete the journal line first, or deactivate the account.";
                }

                // 3. Safe to Delete
                var account = await ctx.SegChartOfAccounts.FindAsync(accountId);
                if (account == null) return "Account not found.";

                ctx.SegChartOfAccounts.Remove(account);
                await ctx.SaveChangesAsync();

                return null; // Success
            }
            catch (Exception ex)
            {
                return $"Error deleting account: {ex.Message}";
            }
        }
        // --- NEW: CSV PARSER HELPER ---
        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var currentToken = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"') { inQuotes = !inQuotes; }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(currentToken.ToString().Trim());
                    currentToken.Clear();
                }
                else { currentToken.Append(c); }
            }
            result.Add(currentToken.ToString().Trim());
            return result;
        }

        // --- UPDATED: IMPORT FULL COA (STRICT SEGMENT 0, FORGIVING SUB-SEGMENTS) ---
        public async Task<string> ImportFullCoaAsync(Guid companyId, Stream fileStream, string fileName)
        {
            try
            {
                // ── 1. Buffer stream (Blazor streams are forward-only; ExcelDataReader needs seek) ──
                using var ms = new MemoryStream();
                await fileStream.CopyToAsync(ms);
                ms.Position = 0;

                // ── 2. Load segment lookup maps (Code → Guid) ──
                using var ctx = await _dbFactory.CreateDbContextAsync();

                var seg0Map = await ctx.Segment0s
                    .Where(x => x.CompanyId == companyId)
                    .ToDictionaryAsync(x => x.Code.Trim().ToUpperInvariant(), x => x.Id);

                var seg1Map = await ctx.Segment1s
                    .Where(x => x.CompanyId == companyId)
                    .ToDictionaryAsync(x => x.Code.Trim().ToUpperInvariant(), x => x.Id);

                var seg2Map = await ctx.Segment2s
                    .Where(x => x.CompanyId == companyId)
                    .ToDictionaryAsync(x => x.Code.Trim().ToUpperInvariant(), x => x.Id);

                var seg3Map = await ctx.Segment3s
                    .Where(x => x.CompanyId == companyId)
                    .ToDictionaryAsync(x => x.Code.Trim().ToUpperInvariant(), x => x.Id);

                var seg4Map = await ctx.Segment4s
                    .Where(x => x.CompanyId == companyId)
                    .ToDictionaryAsync(x => x.Code.Trim().ToUpperInvariant(), x => x.Id);

                var seg5Map = await ctx.Segment5s
                    .Where(x => x.CompanyId == companyId)
                    .ToDictionaryAsync(x => x.Code.Trim().ToUpperInvariant(), x => x.Id);

                // Account Types map (Description → Id), case-insensitive
                var typesMap = await ctx.Set<SegAccountType>()
                    .ToDictionaryAsync(x => x.Description.Trim().ToUpperInvariant(), x => x.Id);

                // Existing COA codes so we can skip duplicates
                var existingCodes = new HashSet<string>(
                    await ctx.SegChartOfAccounts
                        .Where(x => x.CompanyId == companyId)
                        .Select(x => x.AccountCode.ToUpperInvariant())
                        .ToListAsync(),
                    StringComparer.OrdinalIgnoreCase
                );

                // ── 3. Parse rows from file ──
                // Each row: (accountCode, description, accountType)
                var parsedRows = new List<(string Code, string Description, string TypeRaw)>();
                string ext = Path.GetExtension(fileName).ToLowerInvariant();

                if (ext == ".xlsx" || ext == ".xls")
                {
                    // ExcelDataReader requires this for non-Unicode encodings
                    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                    using var reader = ExcelReaderFactory.CreateReader(ms);
                    var dataset = reader.AsDataSet(new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
                    });

                    if (dataset.Tables.Count == 0)
                        return "Error: The Excel file appears to be empty.";

                    var table = dataset.Tables[0];
                    for (int i = 0; i < table.Rows.Count; i++)
                    {
                        var raw0 = table.Rows[i][0]?.ToString()?.Trim() ?? "";
                        var raw1 = table.Rows[i].ItemArray.Length > 1 ? table.Rows[i][1]?.ToString()?.Trim() ?? "" : "";
                        var raw2 = table.Rows[i].ItemArray.Length > 2 ? table.Rows[i][2]?.ToString()?.Trim() ?? "" : "";

                        // Skip header row (contains "Account Code" or similar)
                        if (i == 0 && (raw0.Equals("Account Code", StringComparison.OrdinalIgnoreCase)
                                    || raw0.Equals("Code", StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // Skip obviously empty or metadata rows
                        if (string.IsNullOrWhiteSpace(raw0)) continue;

                        parsedRows.Add((raw0, raw1, raw2));
                    }
                }
                else if (ext == ".csv")
                {
                    ms.Position = 0;
                    using var reader = new StreamReader(ms);
                    bool firstLine = true;
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var parts = ParseCsvLine(line);

                        // Skip header
                        if (firstLine && parts.Count > 0 &&
                            (parts[0].Equals("Account Code", StringComparison.OrdinalIgnoreCase)
                          || parts[0].Equals("Code", StringComparison.OrdinalIgnoreCase)))
                        {
                            firstLine = false;
                            continue;
                        }
                        firstLine = false;

                        if (parts.Count < 2) continue;
                        string code = parts[0].Trim();
                        string desc = parts.Count > 1 ? parts[1].Trim() : "";
                        string type = parts.Count > 2 ? parts[2].Trim() : "";

                        if (!string.IsNullOrEmpty(code))
                            parsedRows.Add((code, desc, type));
                    }
                }
                else
                {
                    return "Unsupported file format. Please upload a .csv, .xls, or .xlsx file.";
                }

                if (parsedRows.Count == 0)
                    return "No data rows found in the file. Please check the file content.";

                // ── 4. Process each row ──
                var newAccounts = new List<SegChartOfAccount>();
                var errors = new List<string>(); // Hard failures (segment 0 missing, type missing)
                var warnings = new List<string>(); // Soft notices (sub-segment not found but still imported)
                int added = 0, skipped = 0;

                foreach (var (rawCode, rawDesc, rawType) in parsedRows)
                {
                    // ── 4a. Resolve Account Type (required) ──
                    string typeKey = rawType.Trim().ToUpperInvariant();
                    if (!typesMap.TryGetValue(typeKey, out int typeId))
                    {
                        // Fuzzy: check if any known type contains this string or vice-versa
                        var fuzzy = typesMap.FirstOrDefault(t =>
                            t.Key.Contains(typeKey, StringComparison.OrdinalIgnoreCase) ||
                            typeKey.Contains(t.Key, StringComparison.OrdinalIgnoreCase));

                        if (fuzzy.Key != null)
                        {
                            typeId = fuzzy.Value;
                        }
                        else
                        {
                            errors.Add($"[{rawCode}] Account Type \"{rawType}\" not found. Row skipped.");
                            skipped++;
                            continue;
                        }
                    }

                    // ── 4b. Split account code into segment parts using "/" ONLY ──
                    // We deliberately avoid splitting on "-" because account codes may contain hyphens.
                    var segParts = rawCode.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                          .Select(p => p.Trim().ToUpperInvariant())
                                          .ToArray();

                    if (segParts.Length == 0)
                    {
                        errors.Add($"[{rawCode}] Account code is empty after parsing. Row skipped.");
                        skipped++;
                        continue;
                    }

                    // ── 4c. Segment 0 — STRICT (must exist) ──
                    string s0Code = segParts[0];
                    if (!seg0Map.TryGetValue(s0Code, out Guid s0Id))
                    {
                        errors.Add($"[{rawCode}] Segment 0 code \"{s0Code}\" not found in your Segment 0 list. Row skipped.");
                        skipped++;
                        continue;
                    }

                    // ── 4d. Duplicate check ──
                    string upperCode = rawCode.Trim().ToUpperInvariant();
                    if (existingCodes.Contains(upperCode))
                    {
                        warnings.Add($"[{rawCode}] Account code already exists. Skipped.");
                        skipped++;
                        continue;
                    }

                    // FALLBACK SYSTEM DETECTION LOGIC: Set status to false if mapping onto control account layers
                    // Type 24 = Inventories, Type 25 = Trade Receivables, Type 26 = Trade Payables
                    bool isControlAccountType = typeId == 24 || typeId == 25 || typeId == 26;

                    // ── 4e. Build account record ──
                    var acc = new SegChartOfAccount
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        Segment0Id = s0Id,
                        AccountCode = rawCode.Trim(),
                        Description = string.IsNullOrWhiteSpace(rawDesc) ? rawCode.Trim() : rawDesc.Trim(),
                        SegAccountTypeId = typeId,
                        AllowJournal = !isControlAccountType, // Enforce false for control classifications
                        IsActive = true
                    };

                    // ── 4f. Optional segments — LENIENT (warn if missing, don't block) ──
                    if (segParts.Length > 1)
                    {
                        string code1 = segParts[1];
                        if (seg1Map.TryGetValue(code1, out Guid s1Id))
                            acc.Segment1Id = s1Id;
                        else
                            warnings.Add($"[{rawCode}] Sub-segment 1 code \"{code1}\" not found — left blank.");
                    }

                    if (segParts.Length > 2)
                    {
                        string code2 = segParts[2];
                        if (seg2Map.TryGetValue(code2, out Guid s2Id))
                            acc.Segment2Id = s2Id;
                        else
                            warnings.Add($"[{rawCode}] Sub-segment 2 code \"{code2}\" not found — left blank.");
                    }

                    if (segParts.Length > 3)
                    {
                        string code3 = segParts[3];
                        if (seg3Map.TryGetValue(code3, out Guid s3Id))
                            acc.Segment3Id = s3Id;
                        else
                            warnings.Add($"[{rawCode}] Sub-segment 3 code \"{code3}\" not found — left blank.");
                    }

                    if (segParts.Length > 4)
                    {
                        string code4 = segParts[4];
                        if (seg4Map.TryGetValue(code4, out Guid s4Id))
                            acc.Segment4Id = s4Id;
                        else
                            warnings.Add($"[{rawCode}] Sub-segment 4 code \"{code4}\" not found — left blank.");
                    }

                    if (segParts.Length > 5)
                    {
                        string code5 = segParts[5];
                        if (seg5Map.TryGetValue(code5, out Guid s5Id))
                            acc.Segment5Id = s5Id;
                        else
                            warnings.Add($"[{rawCode}] Sub-segment 5 code \"{code5}\" not found — left blank.");
                    }

                    newAccounts.Add(acc);
                    existingCodes.Add(upperCode); // Guard against duplicates within the file itself
                    added++;
                }

                // ── 5. Bulk save ──
                if (newAccounts.Count > 0)
                {
                    ctx.SegChartOfAccounts.AddRange(newAccounts);
                    await ctx.SaveChangesAsync();
                }

                // ── 6. Build result summary ──
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Import complete. Added: {added} | Skipped: {skipped} | Total rows read: {parsedRows.Count}");

                if (errors.Any())
                {
                    sb.AppendLine($"\n⛔ ERRORS ({errors.Count} rows skipped):");
                    foreach (var e in errors)
                        sb.AppendLine($"  • {e}");
                }

                if (warnings.Any())
                {
                    sb.AppendLine($"\n⚠️ NOTICES ({warnings.Count}):");
                    foreach (var w in warnings.Take(20))
                        sb.AppendLine($"  • {w}");
                    if (warnings.Count > 20)
                        sb.AppendLine($"  ...and {warnings.Count - 20} more notices.");
                }

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return $"Import failed unexpectedly: {ex.Message}";
            }
        }
    }
}