using System.Globalization;
using System.Text;
using AnalystAI.Api.Analysis;
using AnalystAI.Api.Services;

namespace AnalystAI.Tests;

/// <summary>Small uploaded files, built in code so every expected figure can be worked out by hand.</summary>
internal static class Files
{
    public static Frame Read(string csv)
    {
        var parsed = CsvProfiler.Parse(new StringReader(csv));
        return Frame.From(parsed, CsvProfiler.Profile(parsed));
    }

    /// <summary>Four students, three subjects each. Physics is the weakest subject by construction.</summary>
    public static string Students()
    {
        var names = new[] { "Ali Khan", "Sara Ahmed", "Hamza Raza", "Ayesha Noor" };
        var subjects = new[] { ("Mathematics", 70), ("Physics", 45), ("English", 80) };
        var csv = new StringBuilder("student_id,name,subject,marks,attendance,grade\n");
        for (var i = 0; i < names.Length; i++)
            foreach (var (subject, marks) in subjects)
            {
                var score = marks + i * 5;
                var grade = score >= 80 ? "A" : score >= 60 ? "B" : score >= 50 ? "C" : "F";
                csv.Append(CultureInfo.InvariantCulture, $"S{i + 1:000},{names[i]},{subject},{score},{90 - i * 5},{grade}\n");
            }
        return csv.ToString();
    }

    /// <summary>Orders with quantity and unit price but no revenue column.</summary>
    public static string Sales() => """
        Order ID,Order Date,Customer,Product,Category,Quantity,Unit Price,Region
        ORD-1,2026-01-05,Acme,Widget,Hardware,2,$10,North
        ORD-2,2026-01-20,Bolt,Gadget,Hardware,1,$50,South
        ORD-3,2026-02-03,Acme,Service Plan,Services,3,$100,North
        ORD-4,2026-02-14,Core,Widget,Hardware,5,$10,South
        ORD-5,2026-03-01,Bolt,Service Plan,Services,1,$100,North
        ORD-6,2026-03-09,Core,Gadget,Hardware,2,$50,North
        """;

    public static string Employees() => """
        EmpID,Name,Department,Designation,Salary,Age,Attrition
        101,Emp A,Engineering,Senior,150000,34,No
        102,Emp B,Engineering,Lead,170000,41,Yes
        103,Emp C,Sales,Associate,80000,26,No
        104,Emp D,Sales,Senior,95000,38,No
        105,Emp E,Support,Associate,50000,24,Yes
        106,Emp F,Support,Associate,52000,29,No
        """;

    public static string Weather() => """
        date,city,temperature_c,humidity
        2026-01-01,Lahore,12.5,70
        2026-01-01,Karachi,21.0,60
        2026-01-02,Lahore,13.0,68
        2026-01-02,Karachi,22.5,58
        2026-01-03,Lahore,11.0,72
        2026-01-03,Karachi,20.5,61
        2026-01-03,Karachi,20.5,61
        """;
}
