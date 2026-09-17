using AnalystAI.Api.Query;

namespace AnalystAI.Api.Analysis;

/// <summary>Employees: departments, pay, tenure, performance and who has left.</summary>
internal static class Hr
{
    private static readonly string[] NameWords = ["name", "fullname", "employeename", "staffname"];
    private static readonly string[] IdWords = ["employee", "emp", "staff", "id", "code"];
    private static readonly string[] DepartmentWords = ["department", "dept", "division", "team", "function", "unit"];
    private static readonly string[] RoleWords = ["designation", "position", "role", "title", "job", "jobtitle", "level"];
    private static readonly string[] SalaryWords = ["salary", "pay", "compensation", "ctc", "wage", "wages", "income", "annualsalary", "monthlyincome"];
    private static readonly string[] AgeWords = ["age"];
    private static readonly string[] TenureWords = ["experience", "tenure", "yearsatcompany", "service", "years"];
    private static readonly string[] PerformanceWords = ["performance", "rating", "appraisal", "kpi", "score"];
    private static readonly string[] GenderWords = ["gender", "sex"];
    private static readonly string[] AttritionWords = ["attrition", "left", "terminated", "exit", "churn", "resigned", "status", "active", "employment"];
    private static readonly string[] HireWords = ["hire", "hired", "joining", "join", "doj", "start", "onboard"];
    private static readonly string[] AbsenceWords = ["leave", "leaves", "absence", "absent", "absences", "sick", "attendance", "overtime"];
    private static readonly string[] LocationWords = ["location", "city", "office", "branch", "country", "region", "site"];

    private static readonly HashSet<string> Leavers = new(StringComparer.OrdinalIgnoreCase)
        { "yes", "y", "true", "1", "left", "terminated", "resigned", "inactive", "exited", "former", "quit", "separated" };
    private static readonly HashSet<string> Stayers = new(StringComparer.OrdinalIgnoreCase)
        { "no", "n", "false", "0", "active", "current", "employed", "working" };

    public static void Build(Board b)
    {
        var f = b.Frame;

        var name = b.Claim(b.Find(NameWords, c => c.IsLabel && !c.Identifier), "Employee");
        var id = b.Claim(b.Find(IdWords, c => c.Identifier), "Employee ID");
        var department = b.Claim(b.Find(DepartmentWords, c => c.IsGroup(60)), "Department");
        var role = b.Claim(b.Find(RoleWords, c => c.IsLabel && !c.Identifier), "Role");
        var salaryColumn = b.Claim(b.Find(SalaryWords, c => c.IsNumber && !c.Identifier), "Salary");
        var ageColumn = b.Claim(b.Find(AgeWords, c => c.IsNumber), "Age");
        var tenureColumn = b.Claim(b.Find(TenureWords, c => c.IsNumber && !c.Identifier), "Tenure");
        var performanceColumn = b.Claim(b.Find(PerformanceWords, c => c.IsNumber && !c.Identifier), "Performance");
        var gender = b.Claim(b.Find(GenderWords, c => c.IsGroup(6)), "Gender");
        var attrition = b.Claim(b.Find(AttritionWords, c => c.IsLabel && IsLeaverFlag(c)), "Attrition");
        var hired = b.Claim(b.Find(HireWords, c => c.IsDate) ?? f.Date, "Hire date");
        var absenceColumn = b.Claim(b.Find(AbsenceWords, c => c.IsNumber), "Leave / attendance");
        b.Claim(b.Find(LocationWords, c => c.IsGroup(40)), "Location");

        var salary = salaryColumn is null ? null : Measure.Of(salaryColumn);
        var tenure = tenureColumn is null ? null : Measure.Of(tenureColumn);
        var age = ageColumn is null ? null : Measure.Of(ageColumn);
        var performance = performanceColumn is null ? null : Measure.Of(performanceColumn);
        var absence = absenceColumn is null ? null : Measure.Of(absenceColumn);

        var entity = name ?? id;
        var employees = entity?.Distinct ?? f.RowCount;
        var leaverFlags = attrition is null ? null : attrition.Values.Select(v => Leavers.Contains(v) ? true : Stayers.Contains(v) ? false : (bool?)null).ToArray();
        var attritionRate = leaverFlags is null ? (double?)null
            : leaverFlags.Count(x => x.HasValue) is var known and > 0 ? leaverFlags.Count(x => x == true) / (double)known : null;

        // ── Tiles ──
        b.Kpi("Employees", Fmt.Int(employees), "badge");
        if (department is not null) b.Kpi(Fmt.Plural(Fmt.Title(department.Name)), Fmt.Int(department.Distinct), "apartment");

        if (salary is not null && salary.Present.Any())
        {
            var sorted = salary.Present.Order().ToList();
            b.Kpi($"Avg {salary.Title}", Fmt.Compact(sorted.Average(), salary.Unit with { Decimals = 0 }), "payments",
                $"median {Fmt.Compact(Frame.Percentile(sorted, 0.5), salary.Unit with { Decimals = 0 })}");
        }

        if (attritionRate is { } rate)
            b.Kpi("Attrition Rate", Fmt.Pct(rate), "logout", $"from {Fmt.Lower(attrition!.Name)}", iconTone: rate >= 0.2 ? "error" : null);

        if (tenure is not null && tenure.Present.Any())
            b.Kpi($"Avg {tenure.Title}", Fmt.Value(tenure.Present.Average(), tenure.Unit.ForAverage), "timelapse");

        if (performance is not null && performance.Present.Any())
            b.Kpi($"Avg {performance.Title}", Fmt.Value(performance.Present.Average(), performance.Unit.ForAverage with { Decimals = 2 }), "star",
                $"on a {Fmt.Short(performance.Present.Min(), performance.Unit)}–{Fmt.Short(performance.Present.Max(), performance.Unit)} scale");

        if (age is not null && age.Present.Any())
            b.Kpi("Avg Age", Fmt.Value(age.Present.Average(), age.Unit.ForAverage), "cake");

        if (absence is not null && absence.Present.Any())
            b.Kpi($"Avg {absence.Title}", Fmt.Value(absence.Present.Average(), absence.Unit.ForAverage), "event_busy");

        // ── Charts ──
        if (hired is not null)
            b.Chart($"series:count:{hired.Name}", $"Employees by {Fmt.Lower(hired.Name)}", "line", f.Series(hired, null, Agg.Count), Unit.Count, "Hires per period");

        if (salary is not null)
            b.Chart($"hist:{salary.Name}", $"{salary.Title} distribution", "columns", Frame.Histogram(salary), Unit.Count, "Employees in each pay band");

        if (department is not null)
            b.Chart($"count:{department.Name}", $"Employees by {Fmt.Lower(department.Name)}", "bars", f.Group(department, null, Agg.Count, 10), Unit.Count);

        if (department is not null && salary is not null)
            b.Chart($"Mean:{salary.Name}:{department.Name}", $"Average {salary.Lower} by {Fmt.Lower(department.Name)}", "bars",
                f.Group(department, salary, Agg.Mean, 10), salary.Unit with { Decimals = 0 });

        if (department is not null && leaverFlags is not null)
            b.Chart($"attrition:{department.Name}", $"Attrition by {Fmt.Lower(department.Name)}", "bars", AttritionBy(f, department, leaverFlags), Unit.Percent);

        if (gender is not null)
            b.Chart($"count:{gender.Name}", $"{Fmt.Title(gender.Name)} split", "donut", f.Group(gender, null, Agg.Count, 6), Unit.Count);

        if (performance is not null)
            b.Chart($"hist:{performance.Name}", $"{performance.Title} distribution", "columns", Frame.Histogram(performance), Unit.Count, "Employees at each level");

        // ── Table ──
        b.Table = department is not null
            ? DepartmentTable(f, department, salary, tenure ?? age, leaverFlags)
            : RosterTable(f, entity, role, salaryColumn, performanceColumn ?? tenureColumn);

        // ── Insights ──
        if (department is not null)
        {
            var sizes = f.Group(department, null, Agg.Count, 100);
            if (sizes.Count >= 2)
                b.Insight(1, "neutral", "apartment", $"Largest {Fmt.Lower(department.Name)}",
                    $"{sizes[0].Label} is the largest {Fmt.Lower(department.Name)} with {Fmt.Int(sizes[0].Value)} employees " +
                    $"({DatasetAnalyzer.Share(sizes[0].Value, f.RowCount)}); {sizes[^1].Label} is the smallest with {Fmt.Int(sizes[^1].Value)}.");

            if (salary is not null)
            {
                var pay = f.Group(department, salary, Agg.Mean, 100);
                if (pay.Count >= 2 && pay[^1].Value > 0)
                    b.Insight(2, "neutral", "payments", "Pay gap",
                        $"{pay[0].Label} has the highest average {salary.Lower} ({Fmt.Compact(pay[0].Value, salary.Unit with { Decimals = 0 })}), " +
                        $"{pay[0].Value / pay[^1].Value:0.0}× that of {pay[^1].Label} ({Fmt.Compact(pay[^1].Value, salary.Unit with { Decimals = 0 })}).");
            }
        }

        if (attritionRate is { } overall)
        {
            var body = $"Attrition is {Fmt.Pct(overall)} across {Fmt.Int(leaverFlags!.Count(x => x.HasValue))} employees with a recorded status.";
            if (department is not null)
            {
                var worst = AttritionBy(f, department, leaverFlags).FirstOrDefault();
                if (worst is not null && worst.Value > 0) body += $" It is highest in {worst.Label} ({worst.Value:0.#}%).";
            }
            b.Insight(3, overall >= 0.15 ? "alert" : "positive", "logout", "Attrition", body);
        }

        if (salary is not null && (performance ?? tenure) is { } driver && f.Correlation(salary, driver) is { } r && Math.Abs(r) >= 0.3)
            b.Insight(4, "neutral", "insights", "What pay follows",
                r > 0
                    ? $"{salary.Title} rises with {driver.Lower} (r = {r:0.00})."
                    : $"{salary.Title} falls as {driver.Lower} rises (r = {r:0.00}), which is worth a closer look.");

        if (gender is not null)
        {
            var split = f.Group(gender, null, Agg.Count, 6);
            if (split.Count >= 2)
                b.Insight(5, "neutral", "diversity_3", "Workforce mix",
                    string.Join(", ", split.Select(s => $"{s.Label} {DatasetAnalyzer.Share(s.Value, gender.Present)}")) + " of employees with a recorded " + Fmt.Lower(gender.Name) + ".");
        }
    }

    private static bool IsLeaverFlag(Column c)
    {
        var distinct = c.Values.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count is >= 2 and <= 4
               && distinct.All(v => Leavers.Contains(v) || Stayers.Contains(v))
               && distinct.Any(Leavers.Contains) && distinct.Any(Stayers.Contains);
    }

    private static List<Figure> AttritionBy(Frame f, Column group, bool?[] flags)
    {
        var buckets = new Dictionary<string, (int Left, int Known)>(StringComparer.OrdinalIgnoreCase);
        for (var r = 0; r < f.RowCount; r++)
        {
            if (flags[r] is not { } left || group.Values[r].Length == 0) continue;
            var b = buckets.GetValueOrDefault(group.Values[r]);
            buckets[group.Values[r]] = (b.Left + (left ? 1 : 0), b.Known + 1);
        }
        return buckets
            .Select(kv => new Figure(kv.Key, Math.Round(kv.Value.Left * 100.0 / kv.Value.Known, 1)))
            .OrderByDescending(x => x.Value)
            .Take(10)
            .ToList();
    }

    private static Contracts.DataTableDto DepartmentTable(Frame f, Column department, Measure? salary, Measure? years, bool?[]? flags)
    {
        var counts = f.Group(department, null, Agg.Count, 8);
        var pay = salary is null ? null : f.Group(department, salary, Agg.Mean, 1000).ToDictionary(x => x.Label, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var age = years is null ? null : f.Group(department, years, Agg.Mean, 1000).ToDictionary(x => x.Label, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var attrition = flags is null ? null : AttritionBy(f, department, flags).ToDictionary(x => x.Label, x => x.Value, StringComparer.OrdinalIgnoreCase);

        var columns = new List<(string, string)> { (Fmt.Title(department.Name), "left"), ("Employees", "right") };
        if (salary is not null) columns.Add(($"Avg {salary.Title}", "right"));
        if (years is not null) columns.Add(($"Avg {years.Title}", "right"));
        if (attrition is not null) columns.Add(("Attrition", "right"));

        return DatasetAnalyzer.Table("Employee Overview", $"{counts.Count} of {Fmt.Int(department.Distinct)} {Fmt.Plural(Fmt.Lower(department.Name))} by headcount", columns,
            counts.Select(c =>
            {
                var cells = new List<string> { c.Label, Fmt.Int(c.Value) };
                if (pay is not null) cells.Add(pay.TryGetValue(c.Label, out var p) ? Fmt.Compact(p, salary!.Unit with { Decimals = 0 }) : "—");
                if (age is not null) cells.Add(age.TryGetValue(c.Label, out var a) ? Fmt.Value(a, years!.Unit.ForAverage) : "—");
                if (attrition is not null) cells.Add(attrition.TryGetValue(c.Label, out var t) ? $"{t:0.#}%" : "—");
                return cells.ToArray();
            }));
    }

    private static Contracts.DataTableDto RosterTable(Frame f, Column? entity, Column? role, Column? salary, Column? extra)
    {
        var shown = new[] { entity, role, salary, extra }.Where(c => c is not null).Select(c => c!).ToList();
        if (shown.Count == 0) shown = f.Columns.Take(4).ToList();

        var rows = salary is null
            ? Enumerable.Range(0, Math.Min(8, f.RowCount))
            : Enumerable.Range(0, f.RowCount).Where(r => salary.Numbers[r].HasValue).OrderByDescending(r => salary.Numbers[r]).Take(8);

        return DatasetAnalyzer.Table("Employee Overview",
            salary is null ? $"First 8 of {Fmt.Int(f.RowCount)} employees" : $"Highest 8 of {Fmt.Int(f.RowCount)} by {Fmt.Lower(salary.Name)}",
            shown.Select(c => (Fmt.Title(c.Name), c.IsNumber ? "right" : "left")),
            rows.Select(r => shown.Select(c => General.Cell(c, r)).ToArray()));
    }
}
