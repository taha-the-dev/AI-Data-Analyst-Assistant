using AnalystAI.Api.Query;

namespace AnalystAI.Api.Analysis;

/// <summary>
/// Student and class records: marks, subjects, grades, attendance.
///
/// Handles both shapes a mark sheet comes in — one row per student per subject
/// with a marks column, and one row per student with a column per subject.
/// </summary>
internal static class Education
{
    private static readonly string[] NameWords = ["name", "fullname", "studentname"];
    private static readonly string[] StudentWords = ["student", "pupil", "learner", "candidate"];
    private static readonly string[] SubjectWords = ["subject", "course", "paper", "module", "discipline"];
    private static readonly string[] ClassWords = ["class", "section", "batch", "cohort", "division", "standard", "stream"];
    private static readonly string[] ScoreWords = ["marks", "mark", "score", "scores", "percentage", "percent", "points", "obtained", "total", "gpa", "cgpa", "result"];
    private static readonly string[] AttendanceWords = ["attendance", "attended", "present", "presence"];
    private static readonly string[] GradeWords = ["grade", "letter"];
    private static readonly string[] ResultWords = ["result", "status", "outcome", "pass", "passed", "remarks"];
    private static readonly string[] DayWords = ["days", "day", "sessions", "classes", "lectures", "periods"];

    /// <summary>Subjects a one-column-per-subject mark sheet tends to use.</summary>
    private static readonly string[] SubjectNames =
    [
        "math", "maths", "mathematics", "science", "physics", "chemistry", "biology", "english", "urdu", "hindi",
        "arabic", "french", "spanish", "german", "history", "geography", "computer", "computing", "art", "music",
        "economics", "accounting", "business", "islamiat", "islamic", "pakistan", "social", "civics", "literature",
        "statistics", "programming", "sociology", "psychology", "reading", "writing", "language",
    ];

    private static readonly HashSet<string> PassValues = new(StringComparer.OrdinalIgnoreCase)
        { "pass", "passed", "p", "promoted", "qualified", "yes", "y", "true" };
    private static readonly HashSet<string> FailValues = new(StringComparer.OrdinalIgnoreCase)
        { "fail", "failed", "f", "detained", "reappear", "not qualified", "no", "n", "false" };

    private const double Threshold = 50;

    public static void Build(Board b)
    {
        var f = b.Frame;

        var attendanceColumn = b.Claim(b.Find(AttendanceWords, c => c.IsNumber), "Attendance");
        var scoreColumn = b.Claim(b.Find(ScoreWords, c => c.IsNumber && !c.Identifier), "Score");
        var name = b.Claim(
            b.Find(NameWords, c => c.IsLabel && !c.Identifier) ?? b.Find(StudentWords, c => c.IsLabel && !c.Identifier),
            "Student");
        var id = b.Claim(b.Find([.. StudentWords, "roll", "id", "registration", "enrollment"], c => c.Identifier), "Student ID");
        var subject = b.Claim(b.Find(SubjectWords, c => c.IsGroup(60)), "Subject");
        var cls = b.Claim(b.Find(ClassWords, c => c.IsGroup(60)), "Class");
        var grade = b.Claim(b.Find(GradeWords, c => c.IsLabel && c.Distinct <= 20), "Grade");
        var result = b.Claim(b.Find(ResultWords, c => c.IsLabel && IsPassFail(c)), "Result");
        var date = b.Claim(f.Date, "Date");

        // A column per subject: each is a subject's marks, and a student's
        // overall mark is the average of the ones they have.
        var subjectColumns = f.Columns
            .Where(c => c.IsNumber && !c.Identifier && !b.Claimed(c) && c.Match(SubjectNames) > 0)
            .ToList();
        foreach (var c in subjectColumns) b.Claim(c, "Subject marks");

        Measure? marks = scoreColumn is not null ? Measure.Of(scoreColumn) : null;
        if (marks is null && subjectColumns.Count >= 2)
        {
            var means = new double?[f.RowCount];
            for (var r = 0; r < f.RowCount; r++)
            {
                var present = subjectColumns.Where(c => c.Numbers[r].HasValue).Select(c => c.Numbers[r]!.Value).ToList();
                if (present.Count > 0) means[r] = Frame.Round(present.Average());
            }
            marks = new Measure("Marks", means, Measure.Of(subjectColumns[0]).Unit.ForAverage);
        }

        var entity = name ?? id;
        var students = entity?.Distinct ?? f.RowCount;
        var repeated = entity is not null && f.RowCount > students * 1.2;
        var attendance = attendanceColumn is null ? null : AttendanceMeasure(attendanceColumn);
        var scores = marks?.Present.ToList() ?? [];

        // ── Tiles ──
        b.Kpi(entity is null ? "Records" : "Total Students", Fmt.Int(students), "groups",
            repeated ? $"{Fmt.Int(f.RowCount)} records" : null);

        if (marks is not null && scores.Count > 0)
        {
            var who = entity is null ? null : (Func<double, string?>)(target => Holder(f, entity, marks, target));
            b.Kpi($"Average {marks.Title}", Fmt.Value(scores.Average(), marks.Unit.ForAverage), "functions",
                subjectColumns.Count >= 2 && scoreColumn is null ? $"across {subjectColumns.Count} subjects" : $"of {Fmt.Int(scores.Count)} scores");
            b.Kpi($"Highest {marks.Title}", Fmt.Value(scores.Max(), marks.Unit), "arrow_upward", who?.Invoke(scores.Max()));
            b.Kpi($"Lowest {marks.Title}", Fmt.Value(scores.Min(), marks.Unit), "arrow_downward", who?.Invoke(scores.Min()));
        }

        if (attendance is not null && attendance.Present.Any())
            b.Kpi("Avg Attendance", Fmt.Value(attendance.Present.Average(), attendance.Unit), "event_available",
                attendance.Unit.Suffix == "%" ? null : "days");

        var pass = PassRate(f, result, grade, marks);
        if (pass is not null)
            b.Kpi("Pass Rate", Fmt.Pct(pass.Value.Rate), "verified", pass.Value.Basis, iconTone: pass.Value.Rate < 0.5 ? "error" : null);

        if (subject is not null) b.Kpi(Fmt.Plural(Fmt.Title(subject.Name)), Fmt.Int(subject.Distinct), "menu_book");
        else if (subjectColumns.Count >= 2) b.Kpi("Subjects", Fmt.Int(subjectColumns.Count), "menu_book");
        if (cls is not null) b.Kpi(Fmt.Plural(Fmt.Title(cls.Name)), Fmt.Int(cls.Distinct), "school");

        // ── Charts ──
        List<Figure> bySubject = [];
        string subjectNoun = "subject";
        if (marks is not null)
        {
            if (subject is not null)
            {
                bySubject = f.Group(subject, marks, Agg.Mean, 12);
                subjectNoun = Fmt.Lower(subject.Name);
            }
            else if (subjectColumns.Count >= 2)
            {
                bySubject = subjectColumns
                    .Select(c => new Figure(Fmt.Title(c.Name), Frame.Round(Measure.Of(c).Present.DefaultIfEmpty().Average())))
                    .OrderByDescending(x => x.Value)
                    .ToList();
            }
        }

        if (date is not null && marks is not null)
            b.Chart($"series:{marks.Name}", $"{marks.Title} over time", "line", f.Series(date, marks, Agg.Mean), marks.Unit.ForAverage, "Average per period");

        if (marks is not null)
            b.Chart($"hist:{marks.Name}", "Student performance distribution", "columns", Frame.Histogram(marks), Unit.Count,
                subjectColumns.Count >= 2 && scoreColumn is null ? "Students by average marks" : $"Scores in each {marks.Lower} band");

        if (bySubject.Count >= 2)
            b.Chart($"Mean:{marks!.Name}:{subjectNoun}", $"Average {marks.Lower} by {subjectNoun}", "bars", bySubject, marks.Unit.ForAverage);

        if (grade is not null)
            b.Chart($"count:{grade.Name}", "Grade distribution", grade.Distinct <= 6 ? "donut" : "bars",
                f.Group(grade, null, Agg.Count, 12).OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList(), Unit.Count);
        else if (result is not null)
            b.Chart($"count:{result.Name}", "Result split", "donut", f.Group(result, null, Agg.Count, 6), Unit.Count);

        if (attendance is not null)
            b.Chart($"hist:{attendance.Name}", "Attendance distribution", "columns", Frame.Histogram(attendance), Unit.Count, "Records in each band");

        if (cls is not null && marks is not null)
            b.Chart($"Mean:{marks.Name}:{cls.Name}", $"Average {marks.Lower} by {Fmt.Lower(cls.Name)}", "bars", f.Group(cls, marks, Agg.Mean, 12), marks.Unit.ForAverage);

        // ── Table ──
        b.Table = PerformanceTable(f, entity, subject, cls, scoreColumn, subjectColumns, marks, grade, attendanceColumn);

        // ── Insights ──
        if (bySubject.Count >= 2)
        {
            var low = bySubject[^1];
            var high = bySubject[0];
            b.Insight(1, "neutral", "menu_book", "Weakest subject",
                $"{low.Label} has the lowest average {marks!.Lower} among the {bySubject.Count} {Fmt.Plural(subjectNoun)} " +
                $"({Fmt.Value(low.Value, marks.Unit.ForAverage)}, against {Fmt.Value(high.Value, marks.Unit.ForAverage)} in {high.Label}).");
        }

        if (pass is { } p)
        {
            if (p.FromThreshold && marks is not null)
            {
                string body;
                if (repeated && entity is not null)
                {
                    var below = Enumerable.Range(0, f.RowCount)
                        .Where(r => marks.Values[r] < Threshold && entity.Values[r].Length > 0)
                        .Select(r => entity.Values[r])
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    body = below == 0
                        ? $"Every score is {Threshold:0} or above."
                        : $"{Fmt.Int(below)} of {Fmt.Int(students)} students have at least one score below {Threshold:0} ({Fmt.Int(p.Failing)} of {Fmt.Int(scores.Count)} score records).";
                }
                else
                {
                    body = p.Failing == 0
                        ? $"Every {(entity is null ? "record" : "student")} scored {Threshold:0} or above."
                        : $"{Fmt.Int(p.Failing)} {(entity is null ? "records" : "students")} ({Fmt.Pct(p.Failing / (double)scores.Count)}) scored below {Threshold:0} {marks.Lower}.";
                }
                b.Insight(2, p.Failing == 0 ? "positive" : "alert", p.Failing == 0 ? "check_circle" : "warning", "Below threshold", body);
            }
            else
            {
                var failing = grade is not null && result is null
                    ? $"{Fmt.Int(p.Failing)} {(p.Failing == 1 ? "record has" : "records have")} an F grade"
                    : $"{Fmt.Int(p.Failing)} {(p.Failing == 1 ? "record is" : "records are")} marked as failing in {Fmt.Lower(result!.Name)}";
                b.Insight(2, p.Failing == 0 ? "positive" : "alert", "fact_check", "Pass rate",
                    $"{failing}, a pass rate of {Fmt.Pct(p.Rate)}.");
            }
        }

        if (attendance is not null && attendance.Present.Any())
        {
            var body = $"Average attendance is {Fmt.Value(attendance.Present.Average(), attendance.Unit)} across the dataset.";
            if (attendance.Unit.Suffix == "%")
            {
                var low = attendance.Present.Count(v => v < 75);
                if (low > 0) body += $" {Fmt.Int(low)} {(low == 1 ? "record is" : "records are")} below 75%.";
            }
            if (marks is not null && f.Correlation(attendance, marks) is { } r && Math.Abs(r) >= 0.3)
                body += r > 0
                    ? $" Attendance and {marks.Lower} move together (r = {r:0.00}): students who attend more tend to score higher."
                    : $" Higher attendance goes with lower {marks.Lower} here (r = {r:0.00}), which is worth checking.";
            b.Insight(3, "neutral", "event_available", "Attendance", body);
        }

        if (entity is not null && marks is not null)
        {
            var best = f.Group(entity, marks, Agg.Mean, 1);
            if (best.Count == 1)
                b.Insight(4, "positive", "emoji_events", "Top performer",
                    $"{best[0].Label} has the highest {(repeated ? "average " : "")}{marks.Lower} at {Fmt.Value(best[0].Value, marks.Unit.ForAverage)}.");
        }

        if (grade is not null)
        {
            var counts = f.Group(grade, null, Agg.Count, 1);
            if (counts.Count == 1 && grade.Distinct >= 2)
                b.Insight(5, "neutral", "grade", "Most common grade",
                    $"Grade {counts[0].Label} is the most common, held by {DatasetAnalyzer.Share(counts[0].Value, grade.Present)} of records.");
        }
    }

    /// <summary>
    /// Attendance as a percentage where that is what it is — a 0–1 fraction is
    /// scaled up — and as a plain count where the header says days or the
    /// values run past 100.
    /// </summary>
    private static Measure AttendanceMeasure(Column c)
    {
        var values = c.Numbers;
        var max = values.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty().Max();

        if (max <= 1 && c.Match(DayWords) == 0)
            return new Measure(c.Name, values.Select(v => v * 100).ToArray(), Unit.Percent);
        if (c.Percent || (max <= 100 && c.Match(DayWords) == 0))
            return new Measure(c.Name, values, Unit.Percent);
        return new Measure(c.Name, values, new Unit("", "", 1));
    }

    private static bool IsPassFail(Column c)
    {
        var distinct = c.Values.Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count is >= 1 and <= 4
               && distinct.All(v => PassValues.Contains(v) || FailValues.Contains(v))
               && distinct.Any(v => v.Length > 1);
    }

    private readonly record struct Pass(double Rate, int Failing, string Basis, bool FromThreshold);

    /// <summary>
    /// A pass rate only where the file supports one: a pass/fail column, grades
    /// that include an F, or marks on a 100-point scale measured against 50.
    /// Otherwise there is no pass rate, not an invented one.
    /// </summary>
    private static Pass? PassRate(Frame f, Column? result, Column? grade, Measure? marks)
    {
        if (result is not null)
        {
            var passed = result.Values.Count(PassValues.Contains);
            var failed = result.Values.Count(FailValues.Contains);
            if (passed + failed > 0)
                return new Pass(passed / (double)(passed + failed), failed, $"from {Fmt.Lower(result.Name)}", false);
        }

        if (grade is not null)
        {
            var failed = grade.Values.Count(v => v.Equals("F", StringComparison.OrdinalIgnoreCase) || v.Equals("Fail", StringComparison.OrdinalIgnoreCase));
            if (failed > 0 && grade.Present > 0)
                return new Pass(1 - failed / (double)grade.Present, failed, "grades other than F", false);
        }

        if (marks is not null)
        {
            var scores = marks.Present.ToList();
            if (scores.Count > 0 && scores.Min() >= 0 && scores.Max() <= 100 && scores.Max() > 50)
            {
                var failing = scores.Count(s => s < Threshold);
                return new Pass(1 - failing / (double)scores.Count, failing, $"{marks.Lower} ≥ {Threshold:0} of 100", true);
            }
        }

        return null;
    }

    /// <summary>Who holds a given mark, for the highest and lowest tiles.</summary>
    private static string? Holder(Frame f, Column entity, Measure marks, double target)
    {
        var rows = Enumerable.Range(0, f.RowCount).Where(r => marks.Values[r] is { } v && Math.Abs(v - target) < 1e-9).ToList();
        if (rows.Count == 0) return null;
        var who = entity.Values[rows[0]];
        if (who.Length == 0) return null;
        return rows.Count == 1 ? who : $"{who} +{rows.Count - 1} more";
    }

    private static Contracts.DataTableDto PerformanceTable(
        Frame f, Column? entity, Column? subject, Column? cls, Column? scoreColumn,
        List<Column> subjectColumns, Measure? marks, Column? grade, Column? attendance)
    {
        var columns = new List<(string Label, string Align, Func<int, string> Cell)>();
        if (entity is not null) columns.Add(("Student", "left", r => General.Cell(entity, r)));
        if (subject is not null) columns.Add((Fmt.Title(subject.Name), "left", r => General.Cell(subject, r)));
        if (cls is not null && columns.Count < 3) columns.Add((Fmt.Title(cls.Name), "left", r => General.Cell(cls, r)));

        if (scoreColumn is not null)
            columns.Add((Fmt.Title(scoreColumn.Name), "right", r => General.Cell(scoreColumn, r)));
        else
        {
            foreach (var c in subjectColumns.Take(3)) columns.Add((Fmt.Title(c.Name), "right", r => General.Cell(c, r)));
            if (marks is not null) columns.Add(("Average", "right", r => marks.Values[r] is { } v ? Fmt.Value(v, marks.Unit) : "—"));
        }

        if (grade is not null) columns.Add((Fmt.Title(grade.Name), "left", r => General.Cell(grade, r)));
        if (attendance is not null && columns.Count < 6) columns.Add((Fmt.Title(attendance.Name), "right", r => General.Cell(attendance, r)));

        var rows = marks is null
            ? Enumerable.Range(0, Math.Min(8, f.RowCount))
            : Enumerable.Range(0, f.RowCount).Where(r => marks.Values[r].HasValue).OrderByDescending(r => marks.Values[r]).Take(8);

        return DatasetAnalyzer.Table(
            "Student Performance",
            marks is null ? $"First 8 of {Fmt.Int(f.RowCount)} records" : $"Top 8 of {Fmt.Int(f.RowCount)} records by {marks.Lower}",
            columns.Take(6).Select(c => (c.Label, c.Align)),
            rows.Select(r => columns.Take(6).Select(c => c.Cell(r)).ToArray()));
    }
}
