using CsvHelper;
using System.Globalization;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using SWA;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder();

string connection = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApContext>(options => options.UseNpgsql(connection));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(config =>
{
    config.DocumentName = "SWA";
    config.Title = "SWA v1";
    config.Version = "v1";
});

var app = builder.Build();

app.UseOpenApi();
app.UseSwaggerUi(config =>
{
    config.DocumentTitle = "SWA";
    config.Path = "/swagger";
    config.DocumentPath = "/swagger/{documentName}/swagger.json";
    config.DocExpansion = "list";
});

app.MapPost("/upload", async (IFormFileCollection files, ApContext db) =>
{
    if (files.Count == 0)
        return Results.BadRequest("Отсутствуют файлы для загрузки");

    var uploadPath = $"{Directory.GetCurrentDirectory()}/uploads";
    Directory.CreateDirectory(uploadPath);

    var errors = new List<string>();
    string downloaded = "";

    foreach (var file in files)
    {
        bool er = false;
        string fullPath = Path.Combine(uploadPath, file.FileName);
        using (var fileStream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(fileStream);
        }
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";"
        };
        var records = new List<CsvModel>();
        try
        {
            using var reader = new StreamReader(fullPath);
            using var csv = new CsvReader(reader, config);
            records = csv.GetRecords<CsvModel>().ToList();
        }
        catch (TypeConverterException ex)
        {
            er = true;
            errors.Add($"В файле {file.FileName} ошибка формата данных: {ex.Message}");
        }
        catch (CsvHelper.MissingFieldException ex)
        {
            er = true;
            errors.Add($"В загруженном файле {file.FileName} отсутствуют необходимые поля: {ex.Message}");
        }
        catch (Exception ex)
        {
            er = true;
            errors.Add($"Произошла неизвестная ошибка при загрузке файла {file.FileName}: {ex.Message}");
        }

        if(records.Count < 1 || records.Count > 10000)
        {
            er = true;
            errors.Add($"В файле {file.FileName} ошибка, количество строк не может быть меньше 1 и больше 10 000");
        }
        else
        {
            foreach (var record in records)
            {
                record.Date = record.Date.ToUniversalTime();
                if(record.Date > DateTime.Today || record.Date < new DateTime(2000, 1, 1))
                {
                    er = true;
                    errors.Add($"В файле {file.FileName} ошибка, дата не может быть позже текущей и раньше 01.01.2000");
                }
                if(record.Value < 0)
                {
                    er = true;
                    errors.Add($"В файле {file.FileName} ошибка, значение показателя не может быть меньше 0");
                }
                if(record.ExecutionTime < 0)
                {
                    er = true;
                    errors.Add($"В файле {file.FileName} ошибка, время выполнения не может быть меньше 0");
                }
            }

            if(er) { continue; }
            downloaded = $"{downloaded}{file.FileName} ";
            var fileInValues = await db.ValueEntries.Include(v => v.Values).FirstOrDefaultAsync(f => f.FileName == file.FileName);
            var fileInResult = await db.ResultEntries.FirstOrDefaultAsync(f => f.FileName == file.FileName);
            if(fileInValues==null)
            {
                ValueEntry valEn = new ValueEntry(){FileName = file.FileName, Values=records};
                ResultEntry resEn = new ResultEntry()
                {
                    FileName = file.FileName,
                    DeltaSeconds = (records.Max(r => r.Date) - records.Min(r => r.Date)).TotalSeconds,
                    MinDate = records.Min(r => r.Date),
                    AvgExecutionTime = records.Average(r => r.ExecutionTime),
                    AvgValue = records.Average(r => r.Value),
                    MedianValue = Median.CalculateMedian(records.Select(r => r.Value).ToList()),
                    MaxValue = records.Max(r => r.Value),
                    MinValue = records.Min(r => r.Value)
                };
                await db.ResultEntries.AddAsync(resEn);
                await db.ValueEntries.AddAsync(valEn);
            }
            else
            {
                var oldRecords = fileInValues.Values.ToList();
                db.CsvModel.RemoveRange(oldRecords);
                fileInValues.Values.Clear();
                fileInValues.Values.AddRange(records);

                fileInResult.FileName = file.FileName;
                fileInResult.DeltaSeconds = (records.Max(r => r.Date) - records.Min(r => r.Date)).TotalSeconds;
                fileInResult.MinDate = records.Min(r => r.Date);
                fileInResult.AvgExecutionTime = records.Average(r => r.ExecutionTime);
                fileInResult.AvgValue = records.Average(r => r.Value);
                fileInResult.MedianValue = Median.CalculateMedian(records.Select(r => r.Value).ToList());
                fileInResult.MaxValue = records.Max(r => r.Value);
                fileInResult.MinValue = records.Min(r => r.Value);
            }
            await db.SaveChangesAsync();
        }
    }

    if (errors.Count>0)
    {
        if(downloaded != "") 
        {
            errors.Add($"Но файлы {downloaded}успешно загружены");
        }
        return Results.BadRequest(errors);
    }
    return Results.Ok("Все файлы успешно загружены");
}).Accepts<IFormFileCollection>("multipart/form-data").WithOpenApi().DisableAntiforgery();

app.MapGet("/lastRecords/{filename}", async (string filename, ApContext db) =>
{
    var last10Records = await db.ValueEntries
        .Where(ve => ve.FileName == filename)
        .SelectMany(ve => ve.Values)
        .OrderByDescending(r => r.Date)
        .Take(10)
        .ToListAsync();

    if (last10Records.Count == 0)
        return Results.NotFound($"Записи для файла '{filename}' не найдены.");
    return Results.Ok(last10Records);
});

app.MapGet("/resultFilter", async (
    string? filename,
    DateTime? minStart,
    DateTime? maxStart,
    double? minAvgValue,
    double? maxAvgValue,
    double? minAvgExecutionTime,
    double? maxAvgExecutionTime,
    ApContext db) =>
{
    var resFiltered = await db.ResultEntries
        .Where(r => 
            (filename == null || r.FileName == filename) &&
            (minStart == null || r.MinDate >= minStart) &&
            (maxStart == null || r.MinDate <= maxStart) &&
            (minAvgExecutionTime == null || r.AvgExecutionTime >= minAvgExecutionTime) &&
            (maxAvgExecutionTime == null || r.AvgExecutionTime <= maxAvgExecutionTime) &&
            (minAvgValue == null || r.AvgValue >= minAvgValue) &&
            (maxAvgValue == null || r.AvgValue <= maxAvgValue)
        )
        .ToListAsync();
    if (resFiltered == null || resFiltered.Count == 0)
        return Results.NotFound($"Записи для данного набора фильтров не найдены.");
    return Results.Ok(resFiltered);
});

app.Run();