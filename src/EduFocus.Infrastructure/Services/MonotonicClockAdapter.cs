using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Services;

/// <summary>
/// Cung cấp thời gian thực dựa trên số tick của CPU kể từ khi khởi động hệ thống.
/// Chống mạo danh thời gian (Time-travel protection) do việc đổi giờ hệ thống không ảnh hưởng đến số tick.
/// </summary>
public class MonotonicClockAdapter : IMonotonicClockPort
{
    public TimeSpan GetTickCount()
    {
        // Environment.TickCount64 trả về số milliseconds từ lúc máy bật.
        if (!QueryUnbiasedInterruptTime(out var elapsed100Nanoseconds))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not read unbiased system time.");
        return TimeSpan.FromTicks(checked((long)elapsed100Nanoseconds));
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool QueryUnbiasedInterruptTime(out ulong unbiasedTime);
}
