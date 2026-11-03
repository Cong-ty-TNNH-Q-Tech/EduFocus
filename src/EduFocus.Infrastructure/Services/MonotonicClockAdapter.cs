using System;
using System.Runtime.InteropServices;
using EduFocus.Ports.Outbound.Services;

namespace EduFocus.Infrastructure.Services;

/// <summary>
/// Cung cấp thời gian thực dựa trên số tick của CPU kể từ khi khởi động hệ thống.
/// Chống mạo danh thời gian (Time-travel protection) do việc đổi giờ hệ thống không ảnh hưởng đến số tick.
/// Sử dụng QueryUnbiasedInterruptTime kèm thời gian ngủ hoặc GetTickCount64 để đo thời gian trôi qua thực tế.
/// </summary>
public class MonotonicClockAdapter : IMonotonicClockPort
{
    public TimeSpan GetTickCount()
    {
        if (OperatingSystem.IsWindows())
        {
            // QueryInterruptTime Precise (hoặc GetTickCount64):
            // Dùng QueryInterruptTime để bao gồm cả thời gian hệ thống ở trạng thái Suspend/Sleep,
            // tránh báo động giả ClockJump khi mở lại máy tính sau khi Sleep/Hibernate.
            QueryInterruptTime(out var hundredNanoseconds);
            return TimeSpan.FromTicks((long)Math.Min(hundredNanoseconds, (ulong)long.MaxValue));
        }

        return TimeSpan.FromMilliseconds(Environment.TickCount64);
    }

    [DllImport("kernelbase.dll", ExactSpelling = true)]
    private static extern void QueryInterruptTime(out ulong lpInterruptTime);
}
