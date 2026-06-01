using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Primafit_ERP.Components.Models;
using PrimafitERP.Data;
using System.Reflection;
using System.Text;

namespace Primafit_ERP.Services
{
    public class SegmentsSetupService
    {
        private readonly IDbContextFactory<AppDbContext> _dbFactory;

        public SegmentsSetupService(IDbContextFactory<AppDbContext> dbFactory)
        {
            _dbFactory = dbFactory;
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }

        // =========================================================
        // 1. CONFIGURATION (Active Status & Naming)
        // =========================================================
        public async Task<SegCoaConfig> GetOrCreateConfigAsync(Guid companyId)
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();
            var cfg = await ctx.SegCoaConfigs.FirstOrDefaultAsync(x => x.CompanyId == companyId);

            if (cfg == null)
            {
                cfg = new SegCoaConfig { CompanyId = companyId };
                ctx.SegCoaConfigs.Add(cfg);
                await ctx.SaveChangesAsync();
            }
            return cfg;
        }

        public async Task<string> SaveConfigAsync(SegCoaConfig cfg)
        {
            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var existing = await ctx.SegCoaConfigs.FirstOrDefaultAsync(x => x.Id == cfg.Id);

                if (existing == null)
                {
                    ctx.SegCoaConfigs.Add(cfg);
                }
                else
                {
                    // Copy values
                    ctx.Entry(existing).CurrentValues.SetValues(cfg);
                }
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Error saving config: {ex.Message}";
            }
        }

        // =========================================================
        // 2. SEGMENT VALUES (CRUD)
        // =========================================================

        // --- READ ---
        // Used by the Razor View to load lists (e.g., Service.GetSegmentsAsync<Segment0>(...))
        public async Task<List<T>> GetSegmentsAsync<T>(Guid companyId) where T : class
        {
            using var ctx = await _dbFactory.CreateDbContextAsync();

            // We rely on the fact that your Segment entities follow a naming convention 
            // or we use EF Core's Shadow Properties/Reflection if no interface exists.
            // Since we know they have 'CompanyId' and 'Code', we can use EF.Property in a generic query.

            return await ctx.Set<T>()
                .AsNoTracking()
                .Where(x => EF.Property<Guid>(x, "CompanyId") == companyId)
                .OrderBy(x => EF.Property<string>(x, "Code"))
                .ToListAsync();
        }

        // Wrappers for convenience (if needed by other services)
        public Task<List<Segment0>> GetSegment0Async(Guid cId) => GetSegmentsAsync<Segment0>(cId);
        public Task<List<Segment1>> GetSegment1Async(Guid cId) => GetSegmentsAsync<Segment1>(cId);
        public Task<List<Segment2>> GetSegment2Async(Guid cId) => GetSegmentsAsync<Segment2>(cId);
        public Task<List<Segment3>> GetSegment3Async(Guid cId) => GetSegmentsAsync<Segment3>(cId);
        public Task<List<Segment4>> GetSegment4Async(Guid cId) => GetSegmentsAsync<Segment4>(cId);
        public Task<List<Segment5>> GetSegment5Async(Guid cId) => GetSegmentsAsync<Segment5>(cId);

        // --- SAVE (CREATE / UPDATE) ---
        // The View calls this: await Service.SaveSegmentAsync(new SegmentX { ... })
        public async Task<string> SaveSegmentAsync<T>(T segment) where T : class, new()
        {
            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();

                // Use reflection to get ID and CompanyId
                var idProp = typeof(T).GetProperty("Id");
                var companyIdProp = typeof(T).GetProperty("CompanyId");
                var codeProp = typeof(T).GetProperty("Code");
                var descProp = typeof(T).GetProperty("Description");

                if (idProp == null || companyIdProp == null || codeProp == null || descProp == null)
                    return "System Error: Invalid Segment Model Structure.";

                var idVal = (Guid)idProp.GetValue(segment)!;
                var companyIdVal = (Guid)companyIdProp.GetValue(segment)!;
                var codeVal = (string)codeProp.GetValue(segment)!;
                var descVal = (string)descProp.GetValue(segment)!;

                if (string.IsNullOrWhiteSpace(codeVal)) return "Code is required.";
                if (string.IsNullOrWhiteSpace(descVal)) return "Description is required.";

                // Check for duplicates (Code must be unique within Company)
                var dbSet = ctx.Set<T>();
                var duplicate = await dbSet
                    .AnyAsync(x => EF.Property<Guid>(x, "CompanyId") == companyIdVal
                                && EF.Property<string>(x, "Code") == codeVal
                                && EF.Property<Guid>(x, "Id") != idVal);

                if (duplicate) return $"Code '{codeVal}' already exists.";

                if (idVal == Guid.Empty)
                {
                    // CREATE
                    idProp.SetValue(segment, Guid.NewGuid());
                    dbSet.Add(segment);
                }
                else
                {
                    // UPDATE
                    // Attach and set modified, or fetch and update
                    var existing = await dbSet.FindAsync(idVal);
                    if (existing == null) return "Record not found.";

                    // Update properties safely
                    ctx.Entry(existing).CurrentValues.SetValues(segment);
                }

                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Error saving value: {ex.Message}";
            }
        }

        // --- DELETE ---
        // --- DELETE ---
        public async Task<string> DeleteSegmentAsync<T>(Guid id) where T : class
        {
            try
            {
                using var ctx = await _dbFactory.CreateDbContextAsync();
                var entity = await ctx.Set<T>().FindAsync(id);

                if (entity == null) return "Record not found.";

                // 1. Identify which segment column we are checking based on the generic Type
                var tName = typeof(T).Name;
                var linkedCoasQuery = ctx.SegChartOfAccounts.AsQueryable();

                if (tName == "Segment0") linkedCoasQuery = linkedCoasQuery.Where(c => c.Segment0Id == id);
                else if (tName == "Segment1") linkedCoasQuery = linkedCoasQuery.Where(c => c.Segment1Id == id);
                else if (tName == "Segment2") linkedCoasQuery = linkedCoasQuery.Where(c => c.Segment2Id == id);
                else if (tName == "Segment3") linkedCoasQuery = linkedCoasQuery.Where(c => c.Segment3Id == id);
                else if (tName == "Segment4") linkedCoasQuery = linkedCoasQuery.Where(c => c.Segment4Id == id);
                else if (tName == "Segment5") linkedCoasQuery = linkedCoasQuery.Where(c => c.Segment5Id == id);

                // Fetch the IDs and Codes of any COA using this segment
                var linkedCoas = await linkedCoasQuery.Select(c => new { c.Id, c.AccountCode }).ToListAsync();

                if (linkedCoas.Any())
                {
                    var coaIds = linkedCoas.Select(c => c.Id).ToList();

                    // 2. Check if any of these connected COAs have transactions or drafts
                    bool hasTransactions = await ctx.GLTransactions.AnyAsync(t => coaIds.Contains(t.SegCoaId));
                    bool hasDrafts = await ctx.Set<GLJournalLine>().AnyAsync(l => coaIds.Contains(l.SegCoaId));

                    // Join the account codes for the warning message (limit to first 5 to avoid massive popups)
                    var displayCodes = string.Join(", ", linkedCoas.Select(c => c.AccountCode).Take(5));
                    if (linkedCoas.Count > 5) displayCodes += " and others...";

                    if (hasTransactions || hasDrafts)
                    {
                        return $"Cannot delete: This segment value is used by Account(s) [{displayCodes}] which have existing transactions. You must deactivate those accounts instead.";
                    }
                    else
                    {
                        return $"Cannot delete: This segment value is currently linked to Account(s) [{displayCodes}]. Please delete or edit those accounts first.";
                    }
                }

                ctx.Remove(entity);
                await ctx.SaveChangesAsync();
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        // =========================================================
        // 3. GENERIC IMPORT
        // =========================================================
        public async Task<string> ImportSegmentValuesAsync<T>(Guid companyId, Stream fileStream, string fileName) where T : class, new()
        {
            try
            {
                // 1. CRITICAL FIX: Copy to MemoryStream to make it seekable
                // Blazor streams are forward-only; ExcelDataReader needs to seek (rewind/fast-forward).
                using var memoryStream = new MemoryStream();
                await fileStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0; // Reset pointer to the start

                using var ctx = await _dbFactory.CreateDbContextAsync();
                var dbSet = ctx.Set<T>();

                // Reflection setup (Same as before)
                var type = typeof(T);
                var codeProp = type.GetProperty("Code");
                var descProp = type.GetProperty("Description");
                var compIdProp = type.GetProperty("CompanyId");
                var idProp = type.GetProperty("Id");

                if (codeProp == null || descProp == null || compIdProp == null || idProp == null)
                    return "System Error: Model structure invalid for import.";

                // Get Existing Codes (Same as before)
                var allData = await dbSet
                    .AsNoTracking()
                    .Where(x => EF.Property<Guid>(x, "CompanyId") == companyId)
                    .ToListAsync();

                var existingCodes = new HashSet<string>(
                    allData.Select(x => (string)codeProp.GetValue(x)!),
                    StringComparer.OrdinalIgnoreCase
                );

                List<(string Code, string Description)> parsedRows = new();
                string extension = Path.GetExtension(fileName).ToLower();

                // 2. PARSE (Using memoryStream instead of fileStream)
                if (extension == ".csv")
                {
                    // Use memoryStream here
                    using var reader = new StreamReader(memoryStream);
                    int rowNum = 0;
                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        rowNum++;
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (rowNum == 1 && (line.ToLower().Contains("code") || line.ToLower().Contains("description"))) continue;

                        var parts = ParseCsvLine(line);
                        if (parts.Count >= 2) parsedRows.Add((parts[0].Trim(), parts[1].Trim()));
                    }
                }
                else if (extension == ".xlsx" || extension == ".xls")
                {
                    // Use memoryStream here
                    using var reader = ExcelReaderFactory.CreateReader(memoryStream);
                    var result = reader.AsDataSet();
                    if (result.Tables.Count > 0)
                    {
                        var table = result.Tables[0];
                        for (int i = 0; i < table.Rows.Count; i++)
                        {
                            var firstCell = table.Rows[i][0]?.ToString() ?? "";
                            if (i == 0 && firstCell.ToLower().Contains("code")) continue; // Skip header

                            string code = table.Rows[i][0]?.ToString()?.Trim() ?? "";
                            string desc = table.Rows[i][1]?.ToString()?.Trim() ?? "";

                            if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(desc))
                            {
                                parsedRows.Add((code, desc));
                            }
                        }
                    }
                }
                else
                {
                    return "Unsupported file format. Please use .csv, .xls, or .xlsx";
                }

                // 3. INSERT (Same as before)
                int addedCount = 0;
                int skippedCount = 0;

                foreach (var row in parsedRows)
                {
                    if (existingCodes.Contains(row.Code))
                    {
                        skippedCount++;
                        continue;
                    }

                    var entity = new T();
                    idProp.SetValue(entity, Guid.NewGuid());
                    compIdProp.SetValue(entity, companyId);
                    codeProp.SetValue(entity, row.Code);
                    descProp.SetValue(entity, row.Description);

                    dbSet.Add(entity);
                    existingCodes.Add(row.Code);
                    addedCount++;
                }

                if (addedCount > 0)
                {
                    await ctx.SaveChangesAsync();
                    return $"Success: Imported {addedCount} records. (Skipped {skippedCount} duplicates).";
                }
                else
                {
                    return $"No new records imported. (Skipped {skippedCount} duplicates).";
                }
            }
            catch (Exception ex)
            {
                return $"Import Error: {ex.Message}";
            }
        }


        // Basic CSV Parser handles "Lagos, Main" quotes
        private List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var sb = new StringBuilder();
            foreach (char c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            result.Add(sb.ToString());
            return result;
        }


        // Backward compatibility wrappers if needed by the View for specific Add methods
        public Task<string> AddSegment0Async(Guid cId, string code, string desc) => SaveSegmentAsync(new Segment0 { CompanyId = cId, Code = code, Description = desc });
        public Task<string> AddSegment1Async(Guid cId, string code, string desc) => SaveSegmentAsync(new Segment1 { CompanyId = cId, Code = code, Description = desc });
        public Task<string> AddSegment2Async(Guid cId, string code, string desc) => SaveSegmentAsync(new Segment2 { CompanyId = cId, Code = code, Description = desc });
        public Task<string> AddSegment3Async(Guid cId, string code, string desc) => SaveSegmentAsync(new Segment3 { CompanyId = cId, Code = code, Description = desc });
        public Task<string> AddSegment4Async(Guid cId, string code, string desc) => SaveSegmentAsync(new Segment4 { CompanyId = cId, Code = code, Description = desc });
        public Task<string> AddSegment5Async(Guid cId, string code, string desc) => SaveSegmentAsync(new Segment5 { CompanyId = cId, Code = code, Description = desc });
    }
}