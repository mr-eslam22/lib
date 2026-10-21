using System.Management;
using System.Printing;
using System.Runtime.InteropServices;
using WindowsPrintAutomation.Core.Domain.Enums;
using WindowsPrintAutomation.PrintEngine.Interfaces;

namespace WindowsPrintAutomation.PrintEngine.Adapters;

public class WindowsSpoolerAdapter : IPrinterAdapter
{
    public string AdapterName => "WindowsSpoolerAdapter (Native Win32 / System.Printing)";

    #region Win32 P/Invoke
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pDocName;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)]
        public string? pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern int StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetJob(IntPtr hPrinter, int jobId, int level, IntPtr pJob, int command);

    private const int JOB_CONTROL_PAUSE = 1;
    private const int JOB_CONTROL_RESUME = 2;
    private const int JOB_CONTROL_CANCEL = 3;
    private const int JOB_CONTROL_RESTART = 4;
    private const int JOB_CONTROL_DELETE = 5;
    #endregion

    public Task<PrinterStatusInfo> CheckStatusAsync(string printerName)
    {
        var status = new PrinterStatusInfo
        {
            PrinterName = printerName,
            IsOnline = true,
            HasError = false,
            StatusText = "جاهزة",
            LastCheckedAt = DateTime.UtcNow
        };

        try
        {
            using var server = new LocalPrintServer();
            using var queue = server.GetPrintQueue(printerName);

            queue.Refresh();

            status.QueuedJobsCount = queue.NumberOfJobs;
            status.IsOnline = !queue.IsOffline;

            if (queue.IsOffline)
            {
                status.HasError = true;
                status.DetectedError = ErrorType.PrinterOffline;
                status.StatusText = "الطابعة غير متصلة (Offline)";
            }
            else if (queue.IsPaperJammed)
            {
                status.HasError = true;
                status.DetectedError = ErrorType.PaperJam;
                status.StatusText = "انحشار ورق في الماكينة (Paper Jam)";
            }
            else if (queue.IsOutOfPaper)
            {
                status.HasError = true;
                status.DetectedError = ErrorType.OutOfPaper;
                status.StatusText = "نفاد الورق (Out of Paper)";
            }
            else if (queue.IsInError)
            {
                status.HasError = true;
                status.DetectedError = ErrorType.SpoolerError;
                status.StatusText = "خطأ في طابور الطباعة (Queue Error)";
            }
            else if (queue.IsDoorOpened)
            {
                status.HasError = true;
                status.DetectedError = ErrorType.UnknownPrinterError;
                status.StatusText = "باب الماكينة مفتوح";
            }
            else if (queue.IsPaused)
            {
                status.StatusText = "الطابعة متوقفة مؤقتًا";
            }
        }
        catch (Exception ex)
        {
            status.HasError = true;
            status.DetectedError = ErrorType.UnknownPrinterError;
            status.StatusText = $"تعذر الاستعلام من Spooler: {ex.Message}";
        }

        return Task.FromResult(status);
    }

    public Task<PrintExecutionResult> SendPdfJobAsync(PrintJobRequest request)
    {
        if (!File.Exists(request.PdfPath))
        {
            return Task.FromResult(new PrintExecutionResult
            {
                Success = false,
                ErrorType = ErrorType.InvalidPDF,
                ErrorMessage = $"ملف PDF غير موجود على القرص: {request.PdfPath}",
                InitialVerificationStatus = VerificationStatus.Failed
            });
        }

        IntPtr hPrinter = IntPtr.Zero;
        try
        {
            if (!OpenPrinter(request.PrinterName, out hPrinter, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                return Task.FromResult(new PrintExecutionResult
                {
                    Success = false,
                    ErrorType = ErrorType.PrinterOffline,
                    ErrorMessage = $"تعذر فتح منفذ الطابعة '{request.PrinterName}'. كود الخطأ Win32: {err}",
                    InitialVerificationStatus = VerificationStatus.Failed
                });
            }

            var di = new DOCINFOA
            {
                pDocName = $"{Path.GetFileName(request.PdfPath)} - {request.Job.JobNumber}",
                pDataType = "RAW"
            };

            int spoolerJobId = StartDocPrinter(hPrinter, 1, di);
            if (spoolerJobId == 0)
            {
                int err = Marshal.GetLastWin32Error();
                return Task.FromResult(new PrintExecutionResult
                {
                    Success = false,
                    ErrorType = ErrorType.SpoolerError,
                    ErrorMessage = $"فشل بدء مهمة الطباعة في Spooler. كود الخطأ Win32: {err}",
                    InitialVerificationStatus = VerificationStatus.Failed
                });
            }

            // Stream file bytes to Windows Spooler
            byte[] fileBytes = File.ReadAllBytes(request.PdfPath);
            IntPtr pBytes = Marshal.AllocHGlobal(fileBytes.Length);
            try
            {
                Marshal.Copy(fileBytes, 0, pBytes, fileBytes.Length);

                StartPagePrinter(hPrinter);
                bool writeSuccess = WritePrinter(hPrinter, pBytes, fileBytes.Length, out int bytesWritten);
                EndPagePrinter(hPrinter);
                EndDocPrinter(hPrinter);

                if (!writeSuccess || bytesWritten != fileBytes.Length)
                {
                    int err = Marshal.GetLastWin32Error();
                    return Task.FromResult(new PrintExecutionResult
                    {
                        Success = false,
                        SpoolerJobId = spoolerJobId,
                        ErrorType = ErrorType.SpoolerError,
                        ErrorMessage = $"فشلت كتابة كامل البيانات إلى Spooler. كود الخطأ Win32: {err}",
                        InitialVerificationStatus = VerificationStatus.Failed
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pBytes);
            }

            // CRITICAL: We return Accepted (NOT PhysicallyVerified)!
            return Task.FromResult(new PrintExecutionResult
            {
                Success = true,
                SpoolerJobId = spoolerJobId,
                InitialVerificationStatus = VerificationStatus.Accepted
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new PrintExecutionResult
            {
                Success = false,
                ErrorType = ErrorType.SpoolerError,
                ErrorMessage = $"استثناء أثناء إرسال أمر الطباعة: {ex.Message}",
                InitialVerificationStatus = VerificationStatus.Failed
            });
        }
        finally
        {
            if (hPrinter != IntPtr.Zero)
            {
                ClosePrinter(hPrinter);
            }
        }
    }

    public Task<bool> CancelJobAsync(string printerName, int spoolerJobId)
    {
        return ControlJobAsync(printerName, spoolerJobId, JOB_CONTROL_CANCEL);
    }

    public Task<bool> PauseJobAsync(string printerName, int spoolerJobId)
    {
        return ControlJobAsync(printerName, spoolerJobId, JOB_CONTROL_PAUSE);
    }

    public Task<bool> ResumeJobAsync(string printerName, int spoolerJobId)
    {
        return ControlJobAsync(printerName, spoolerJobId, JOB_CONTROL_RESUME);
    }

    private Task<bool> ControlJobAsync(string printerName, int spoolerJobId, int command)
    {
        IntPtr hPrinter = IntPtr.Zero;
        try
        {
            if (OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                bool success = SetJob(hPrinter, spoolerJobId, 0, IntPtr.Zero, command);
                return Task.FromResult(success);
            }
            return Task.FromResult(false);
        }
        catch
        {
            return Task.FromResult(false);
        }
        finally
        {
            if (hPrinter != IntPtr.Zero)
            {
                ClosePrinter(hPrinter);
            }
        }
    }

    public Task<List<SpoolerJobInfo>> GetActiveQueueJobsAsync(string printerName)
    {
        var list = new List<SpoolerJobInfo>();
        try
        {
            using var server = new LocalPrintServer();
            using var queue = server.GetPrintQueue(printerName);
            queue.Refresh();

            var jobCollection = queue.GetPrintJobInfoCollection();
            foreach (var job in jobCollection)
            {
                list.Add(new SpoolerJobInfo
                {
                    JobId = job.JobIdentifier,
                    DocumentName = job.Name,
                    StatusText = job.JobStatus.ToString(),
                    TotalPages = job.NumberOfPages,
                    PagesPrinted = job.NumberOfPagesPrinted,
                    IsPaused = job.JobStatus.HasFlag(PrintJobStatus.Paused),
                    IsError = job.JobStatus.HasFlag(PrintJobStatus.Error) || job.JobStatus.HasFlag(PrintJobStatus.PaperOut)
                });
            }
        }
        catch
        {
            // If query fails, return empty list
        }
        return Task.FromResult(list);
    }

    public static List<string> GetInstalledWindowsPrinters()
    {
        var list = new List<string>();
        try
        {
            using var server = new LocalPrintServer();
            var queues = server.GetPrintQueues();
            foreach (var q in queues)
            {
                list.Add(q.FullName);
            }
        }
        catch
        {
            // Fallback to WMI if PrintServer has permissions issues
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Printer");
                foreach (var item in searcher.Get())
                {
                    var name = item["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        list.Add(name);
                    }
                }
            }
            catch
            {
                // return whatever was found
            }
        }
        return list;
    }
}
