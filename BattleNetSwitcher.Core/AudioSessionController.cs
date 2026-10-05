#nullable enable
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using BattleNetSwitcher.Interop;

namespace BattleNetSwitcher.Core
{
    /// <summary>
    /// 通过 WASAPI 会话枚举，将匹配进程名的音频会话静音 / 取消静音。
    /// 因为音频会话通常在应用首次提交音频后才注册，这里做多次重试。
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class AudioSessionController
    {
        /// <summary>
        /// 异步多次尝试静音 / 取消静音，覆盖音频会话延迟注册的问题。
        /// </summary>
        /// <param name="processName">目标进程名（不含 .exe）</param>
        /// <param name="mute">true=静音，false=取消静音</param>
        /// <param name="attempts">重试次数</param>
        /// <param name="intervalMs">重试间隔（毫秒）</param>
        public static void MuteByNameAsync(string processName, bool mute,
                                           int attempts = 8, int intervalMs = 400)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;

            Task.Run(() =>
            {
                for (int i = 0; i < attempts; i++)
                {
                    if (TrySetMuteOnce(processName, mute))
                    {
                        // 命中后再补一次，覆盖可能延迟出现的其他同名会话
                        TrySetMuteOnce(processName, mute);
                        return;
                    }
                    Thread.Sleep(intervalMs);
                }
            });
        }

        /// <summary>同步尝试一次，返回是否命中至少一个会话。</summary>
        public static bool TrySetMuteOnce(string processName, bool mute)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;

            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

                int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
                if (hr != 0 || device == null) return false;

                Guid iid = typeof(IAudioSessionManager2).GUID;
                hr = device.Activate(ref iid, CoreAudioInterop.CLSCTX_ALL, IntPtr.Zero, out object managerObj);
                if (hr != 0 || managerObj == null) return false;

                var manager = (IAudioSessionManager2)managerObj;

                hr = manager.GetSessionEnumerator(out var sessions);
                if (hr != 0 || sessions == null) return false;

                hr = sessions.GetCount(out int count);
                if (hr != 0) return false;

                bool hit = false;
                for (int i = 0; i < count; i++)
                {
                    if (sessions.GetSession(i, out var session) != 0 || session == null)
                        continue;

                    if (session.GetProcessId(out uint pid) != 0 || pid == 0)
                        continue;

                    string name;
                    try
                    {
                        name = Process.GetProcessById((int)pid).ProcessName;
                    }
                    catch (ArgumentException) { continue; }
                    catch (InvalidOperationException) { continue; }

                    if (!string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 会话对象同时实现 IAudioSessionControl2 和 ISimpleAudioVolume
                    if (session is ISimpleAudioVolume volume)
                    {
                        if (volume.SetMute(mute, IntPtr.Zero) == 0)
                            hit = true;
                    }
                }

                return hit;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("音频会话操作失败: " + ex.Message);
                return false;
            }
        }
    }
}