using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.JobObjects;
using Windows.Win32.System.Threading;

namespace Lucent.Preview.Supervisor;

// All native handles stay private. Child code is resumed only after job assignment succeeds.
internal sealed unsafe class OwnedProcessJob : IDisposable
{
    private HANDLE job;
    private HANDLE process;
    private HANDLE thread;
    private Func<bool>? canObserveJob;
    internal FileStream StandardInput { get; private set; } = null!;
    internal FileStream StandardOutput { get; private set; } = null!;
    internal FileStream StandardError { get; private set; } = null!;
    internal bool Launched { get; private set; }
    internal bool Assigned { get; private set; }

    internal static OwnedProcessJob Start(
        SupervisorRequest request,
        CancellationToken cancellationToken,
        Func<bool>? canObserveJob = null
    )
    {
        var owned = new OwnedProcessJob { canObserveJob = canObserveJob };
        try
        {
            owned.Launch(request, cancellationToken);
            return owned;
        }
        catch
        {
            // Assignment failure leaves an unexecuted suspended child, which still must be reaped.
            try
            {
                owned.KillUnstartedChild();
            }
            finally
            {
                owned.Dispose();
            }
            throw;
        }
    }

    private void Launch(SupervisorRequest request, CancellationToken cancellationToken)
    {
        job = PInvoke.CreateJobObject((SECURITY_ATTRIBUTES*)null, default(PCWSTR));
        if (job.IsNull)
            throw NativeFailure();
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags =
            JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (
            !PInvoke.SetInformationJobObject(
                job,
                JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                &limits,
                (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)
            )
        )
            throw NativeFailure();

        var (inputRead, inputWrite) = CreatePipe();
        using (inputRead)
        {
            StandardInput = OpenParent(inputWrite, FileAccess.Write);
            var (outputRead, outputWrite) = CreatePipe();
            using (outputWrite)
            {
                StandardOutput = OpenParent(outputRead, FileAccess.Read);
                var (errorRead, errorWrite) = CreatePipe();
                using (errorWrite)
                {
                    StandardError = OpenParent(errorRead, FileAccess.Read);
                    var inherited = stackalloc HANDLE[3]
                    {
                        Handle(inputRead),
                        Handle(outputWrite),
                        Handle(errorWrite),
                    };
                    nuint bytes = 0;
                    _ = PInvoke.InitializeProcThreadAttributeList(default, 1, 0, &bytes);
                    if (bytes == 0)
                        throw NativeFailure();
                    var attributes = NativeMemory.Alloc(bytes);
                    var initialized = false;
                    try
                    {
                        var list = (LPPROC_THREAD_ATTRIBUTE_LIST)attributes;
                        if (!PInvoke.InitializeProcThreadAttributeList(list, 1, 0, &bytes))
                            throw NativeFailure();
                        initialized = true;
                        // PROC_THREAD_ATTRIBUTE_HANDLE_LIST: only the three child pipe ends inherit.
                        if (
                            !PInvoke.UpdateProcThreadAttribute(
                                list,
                                0,
                                0x00020002,
                                inherited,
                                (nuint)(3 * sizeof(HANDLE)),
                                null,
                                null
                            )
                        )
                            throw NativeFailure();
                        var startup = new STARTUPINFOEXW
                        {
                            lpAttributeList = list,
                            StartupInfo = new STARTUPINFOW
                            {
                                cb = (uint)sizeof(STARTUPINFOEXW),
                                dwFlags = STARTUPINFOW_FLAGS.STARTF_USESTDHANDLES,
                                hStdInput = inherited[0],
                                hStdOutput = inherited[1],
                                hStdError = inherited[2],
                            },
                        };
                        var commandLine = CommandLine(request.Program, request.Args).ToCharArray();
                        var environment = EnvironmentBlock(request.Environment).ToCharArray();
                        fixed (char* executable = request.Program)
                        fixed (char* command = commandLine)
                        fixed (char* directory = request.WorkingDirectory)
                        fixed (char* variables = environment)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            PROCESS_INFORMATION information;
                            if (
                                !PInvoke.CreateProcess(
                                    executable,
                                    command,
                                    null,
                                    null,
                                    true,
                                    PROCESS_CREATION_FLAGS.CREATE_SUSPENDED
                                        | PROCESS_CREATION_FLAGS.CREATE_NO_WINDOW
                                        | PROCESS_CREATION_FLAGS.CREATE_UNICODE_ENVIRONMENT
                                        | PROCESS_CREATION_FLAGS.EXTENDED_STARTUPINFO_PRESENT,
                                    variables,
                                    directory,
                                    &startup.StartupInfo,
                                    &information
                                )
                            )
                                throw NativeFailure();
                            process = information.hProcess;
                            thread = information.hThread;
                            Launched = true;
                        }
                        if (!PInvoke.AssignProcessToJobObject(job, process))
                            throw NativeFailure();
                        Assigned = true;
                        cancellationToken.ThrowIfCancellationRequested();
                        if (PInvoke.ResumeThread(thread) == uint.MaxValue)
                            throw NativeFailure();
                        PInvoke.CloseHandle(thread);
                        thread = default;
                    }
                    finally
                    {
                        if (initialized)
                            PInvoke.DeleteProcThreadAttributeList(
                                (LPPROC_THREAD_ATTRIBUTE_LIST)attributes
                            );
                        NativeMemory.Free(attributes);
                    }
                }
            }
        }
    }

    internal bool HasExited => PInvoke.WaitForSingleObject(process, 0) == WAIT_EVENT.WAIT_OBJECT_0;

    internal int? ExitCode
    {
        get
        {
            uint code;
            return HasExited && PInvoke.GetExitCodeProcess(process, &code)
                ? unchecked((int)code)
                : null;
        }
    }

    internal bool TryActiveCount(out uint count)
    {
        // Internal fault injection can withhold observation, but cannot bypass the real job or its termination.
        if (canObserveJob is not null && !canObserveJob())
        {
            count = 0;
            return false;
        }
        var accounting = new JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
        var success = PInvoke.QueryInformationJobObject(
            job,
            JOBOBJECTINFOCLASS.JobObjectBasicAccountingInformation,
            &accounting,
            (uint)sizeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION),
            null
        );
        count = accounting.ActiveProcesses;
        return success;
    }

    internal bool Terminate() => PInvoke.TerminateJobObject(job, 1);

    private void KillUnstartedChild()
    {
        if (!Launched)
            return;
        var terminated = Assigned
            ? PInvoke.TerminateJobObject(job, 1)
            : PInvoke.TerminateProcess(process, 1);
        if (!terminated || PInvoke.WaitForSingleObject(process, 5_000) != WAIT_EVENT.WAIT_OBJECT_0)
            throw new TerminationUnconfirmedException();
        if (Assigned && (!TryActiveCount(out var count) || count != 0))
            throw new TerminationUnconfirmedException();
    }

    private static (SafeFileHandle Read, SafeFileHandle Write) CreatePipe()
    {
        var security = new SECURITY_ATTRIBUTES
        {
            nLength = (uint)sizeof(SECURITY_ATTRIBUTES),
            bInheritHandle = true,
        };
        HANDLE read;
        HANDLE write;
        if (!PInvoke.CreatePipe(&read, &write, &security, 0))
            throw NativeFailure();
        return (
            new SafeFileHandle((nint)read.Value, true),
            new SafeFileHandle((nint)write.Value, true)
        );
    }

    private static FileStream OpenParent(SafeFileHandle handle, FileAccess access)
    {
        try
        {
            if (
                !PInvoke.SetHandleInformation(
                    Handle(handle),
                    (uint)HANDLE_FLAGS.HANDLE_FLAG_INHERIT,
                    0
                )
            )
                throw NativeFailure();
            return new FileStream(handle, access, 4096, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static HANDLE Handle(SafeFileHandle handle) => new((void*)handle.DangerousGetHandle());

    private static Win32Exception NativeFailure() => new(Marshal.GetLastPInvokeError());

    internal static string CommandLine(string program, string[] arguments)
    {
        var builder = new StringBuilder(Quote(program));
        foreach (var argument in arguments)
            builder.Append(' ').Append(Quote(argument));
        if (builder.Length >= 32_767)
            throw new ArgumentException("The Windows command line exceeds its bound.");
        return builder.Append('\0').ToString();
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static string EnvironmentBlock(Dictionary<string, string>? overrides)
    {
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (
            System.Collections.DictionaryEntry pair in System.Environment.GetEnvironmentVariables()
        )
            values[(string)pair.Key] = (string?)pair.Value ?? "";
        if (overrides is not null)
            foreach (var pair in overrides)
                values[pair.Key] = pair.Value;
        var result = String.Join('\0', values.Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
        if (result.Length > 65_535)
            throw new ArgumentException("The child environment exceeds its bound.");
        return result;
    }

    public void Dispose()
    {
        // Closing this noninherited job handle is also the final crash-path safety net.
        if (!job.IsNull)
        {
            PInvoke.CloseHandle(job);
            job = default;
        }
        if (!thread.IsNull)
        {
            PInvoke.CloseHandle(thread);
            thread = default;
        }
        if (!process.IsNull)
        {
            PInvoke.CloseHandle(process);
            process = default;
        }
        StandardInput?.Dispose();
        StandardOutput?.Dispose();
        StandardError?.Dispose();
    }
}

internal sealed class TerminationUnconfirmedException : Exception { }
