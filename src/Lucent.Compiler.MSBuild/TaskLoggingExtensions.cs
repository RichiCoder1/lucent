using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Lucent.Compiler.MSBuild;

internal static class TaskLoggingExtensions
{
    public static void LogErrorEvent(
        this TaskLoggingHelper logger,
        BuildErrorEventArgs error) =>
        logger.LogError(
            subcategory: error.Subcategory,
            errorCode: error.Code,
            helpKeyword: error.HelpKeyword,
            file: error.File,
            lineNumber: error.LineNumber,
            columnNumber: error.ColumnNumber,
            endLineNumber: error.EndLineNumber,
            endColumnNumber: error.EndColumnNumber,
            message: error.Message);

    public static void LogWarningEvent(
        this TaskLoggingHelper logger,
        BuildWarningEventArgs warning) =>
        logger.LogWarning(
            subcategory: warning.Subcategory,
            warningCode: warning.Code,
            helpKeyword: warning.HelpKeyword,
            file: warning.File,
            lineNumber: warning.LineNumber,
            columnNumber: warning.ColumnNumber,
            endLineNumber: warning.EndLineNumber,
            endColumnNumber: warning.EndColumnNumber,
            message: warning.Message);
}
