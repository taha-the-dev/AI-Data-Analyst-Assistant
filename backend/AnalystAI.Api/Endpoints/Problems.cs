namespace AnalystAI.Api.Endpoints;

/// <summary>
/// The failures this API returns, written once.
///
/// Every screen shows the server's title and detail verbatim, so the wording is
/// part of the product: each one says what happened and what to do next.
/// </summary>
public static class Problems
{
    public static IResult NoDataset(int? requested) => requested is > 0
        ? Results.Problem(
            title: "Dataset not found",
            detail: $"No dataset with id {requested}. It may have been deleted — pick one from /api/datasets.",
            statusCode: StatusCodes.Status404NotFound)
        : Results.Problem(
            title: "There are no datasets yet",
            detail: "Upload a CSV to /api/datasets/upload and every screen will read from it.",
            statusCode: StatusCodes.Status404NotFound);

    public static IResult NoRows(int datasetId) => Results.Problem(
        title: "That dataset has no rows",
        detail: $"Dataset {datasetId} has a header row but no rows beneath it, so there is nothing to compute from.",
        statusCode: StatusCodes.Status404NotFound);

    /// <summary>A file uploaded before the original was kept has only its sales-shaped rows to read.</summary>
    public static IResult NoSource(string datasetName) => Results.Problem(
        title: "Upload this file again to analyse it",
        detail: $"'{datasetName}' was uploaded before files were kept for analysis, so its own columns were not saved. "
              + "Upload the original file again and every screen will read from all of its columns.",
        statusCode: StatusCodes.Status409Conflict);

    public static IResult NotFound(string what, int id) => Results.Problem(
        title: $"{what} not found",
        detail: $"No {what.ToLowerInvariant()} with id {id}.",
        statusCode: StatusCodes.Status404NotFound);

    public static IResult BadRequest(string title, string detail) => Results.Problem(
        title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

    public static IResult Conflict(string title, string detail) => Results.Problem(
        title: title, detail: detail, statusCode: StatusCodes.Status409Conflict);

    public static IResult UnknownColumn(string what, string value, IEnumerable<string> allowed) => BadRequest(
        $"Unknown {what}",
        $"'{value}' is not a column. Available: {string.Join(", ", allowed)}.");
}
