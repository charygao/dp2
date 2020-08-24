﻿using System;
using System.Collections.Generic;
using System.Deployment.Application;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using DigitalPlatform;
using DigitalPlatform.WPF;
using DigitalPlatform.RFID;
using DigitalPlatform.Text;
using DigitalPlatform.LibraryClient.localhost;

namespace dp2SSL.Models
{
    // 全局监控任务
    public static class GlobalMonitor
    {
        /*
        // 可以适当降低探测的频率。比如每五分钟探测一次
        // 两次检测网络之间的间隔
        static TimeSpan _detectPeriod = TimeSpan.FromMinutes(5);
        // 最近一次检测网络的时间
        static DateTime _lastDetectTime;

        // 两次零星同步之间的间隔
        static TimeSpan _replicatePeriod = TimeSpan.FromMinutes(1);
        // 最近一次零星同步的时间
        static DateTime _lastReplicateTime;


        public static string LibraryNetworkCondition
        {
            get
            {
                return _libraryNetworkCondition;
            }
        }
        */
        static Task _monitorTask = null;

        // 是否已经(升级)更新了
        static bool _updated = false;
        // 最近一次检查升级的时刻
        static DateTime _lastUpdateTime;
        // 检查升级的时间间隔
        static TimeSpan _updatePeriod = TimeSpan.FromMinutes(2 * 60); // 2*60 两个小时

        // 监控间隔时间
        static TimeSpan _monitorIdleLength = TimeSpan.FromSeconds(10);

        static AutoResetEvent _eventMonitor = new AutoResetEvent(false);

        // 激活 Monitor 任务
        public static void ActivateMonitor()
        {
            _eventMonitor.Set();
        }

        // 启动一般监控任务
        public static void StartMonitorTask()
        {
            if (_monitorTask != null)
                return;

            CancellationToken token = App.CancelToken;

            token.Register(() =>
            {
                _eventMonitor.Set();
            });

            _monitorTask = Task.Factory.StartNew(async () =>
            {
                WpfClientInfo.WriteInfoLog("全局监控专用线程开始");
                try
                {
                    while (token.IsCancellationRequested == false)
                    {
                        // await Task.Delay(TimeSpan.FromSeconds(10));
                        _eventMonitor.WaitOne(_monitorIdleLength);

                        token.ThrowIfCancellationRequested();

                        // 检查小票打印机状态
                        var check_result = CheckPosPrinter();
                        if (check_result.Value == -1)
                            App.SetError("printer", "小票打印机状态异常");
                        else if (StringUtil.IsInList("paperout", check_result.ErrorCode))
                            App.SetError("printer", "小票打印机缺纸");
                        else if (StringUtil.IsInList("paperwillout", check_result.ErrorCode))
                            App.SetError("printer", "小票打印机即将缺纸");
                        else
                            App.SetError("printer", null);

                        // 检查升级 dp2ssl
                        if (_updated == false
                        // && StringUtil.IsDevelopMode() == false
                        && ApplicationDeployment.IsNetworkDeployed == false
                        && DateTime.Now - _lastUpdateTime > _updatePeriod)
                        {
                            WpfClientInfo.WriteInfoLog("开始自动检查升级");
                            // result.Value:
                            //      -1  出错
                            //      0   经过检查发现没有必要升级
                            //      1   成功
                            //      2   成功，但需要立即重新启动计算机才能让复制的文件生效
                            var update_result = await GreenInstall.GreenInstaller.InstallFromWeb("http://dp2003.com/dp2ssl/v1_dev",
                                "c:\\dp2ssl",
                                "delayExtract,updateGreenSetupExe",
                                //true,
                                //true,
                                token,
                                null);
                            if (update_result.Value == -1)
                                WpfClientInfo.WriteErrorLog($"自动检查升级出错: {update_result.ErrorInfo}");
                            else
                                WpfClientInfo.WriteInfoLog($"结束自动检查升级 update_result:{update_result.ToString()}");

                            if (update_result.Value == 1 || update_result.Value == 2)
                            {
                                App.TriggerUpdated("重启可使用新版本");
                                _updated = true;
                                PageShelf.TrySetMessage(null, "dp2SSL 升级文件已经下载成功，下次重启时可自动升级到新版本");
                            }
                            _lastUpdateTime = DateTime.Now;

                        }
                    }
                    _monitorTask = null;
                }
                catch (OperationCanceledException)
                {

                }
                catch (Exception ex)
                {
                    WpfClientInfo.WriteErrorLog($"全局监控专用线程出现异常: {ExceptionUtil.GetDebugText(ex)}");
                    App.SetError("global_monitor", $"全局监控专用线程出现异常: {ex.Message}");
                }
                finally
                {
                    WpfClientInfo.WriteInfoLog("全局监控专用线程结束");
                }
            },
token,
TaskCreationOptions.LongRunning,
TaskScheduler.Default);
        }

        // 检查小票打印机状态
        static DigitalPlatform.NormalResult CheckPosPrinter()
        {
            if (string.IsNullOrEmpty(App.PosPrintStyle)
                || App.PosPrintStyle == "不打印")
                return new DigitalPlatform.NormalResult();

            if (string.IsNullOrEmpty(App.RfidUrl)
                || string.IsNullOrEmpty(RfidManager.Url))
                return new DigitalPlatform.NormalResult();

            var result = RfidManager.PosPrint("getstatus", "", "");
            if (result.Value == 0)
            {
                return result;
                /*
                if (StringUtil.IsInList("paperout", result.ErrorCode))
                    return new NormalResult { ValueTask = -1, ErrorCode = result.ErrorCode };
                */
            }
            return result;
        }
    }
}