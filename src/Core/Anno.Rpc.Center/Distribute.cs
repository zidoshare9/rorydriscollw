﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Thrift.Transport;

namespace Anno.Rpc.Center
{
    using Anno.Log;
    public static class Distribute
    {
        /// <summary>
        /// 服务检查通知事件
        /// </summary>
        public static event ServiceNotice CheckNotice = null;
        static readonly ThriftConfig Tc = ThriftConfig.CreateInstance();
        static readonly object LockHelper = new object();
        /// <summary>
        /// 获取微服务地址
        /// </summary>
        /// <param name="channel"></param>
        /// <returns></returns>
        public static List<Micro> GetMicro(string channel)
        {
            List<ServiceInfo> service = new List<ServiceInfo>();
            //if (channel.StartsWith("md5:"))
            //{
            //    long waitTime = 19000;
            //    var md5 = channel.Substring(4);
            //    while(md5.Equals(Tc.ServiceMd5) && waitTime > 0)
            //    {
            //        waitTime = waitTime - 10;
            //        Task.Delay(10).Wait();// Thread.Sleep(10);
            //    }
            //    service = Tc.ServiceInfoList;
            //}

            if (channel.StartsWith("md5:"))
            {
                service = Tc.ServiceInfoList;
            }
            else
            {
                service = Tc.ServiceInfoList.FindAll(i => i.Name.Contains(channel));
            }
            List<Micro> msList = new List<Micro>();
            service.ForEach(s =>
            {
                Micro micro = new Micro
                {
                    Ip = s.Ip,
                    Port = s.Port,
                    Timeout = s.Timeout,
                    Name = s.Name,
                    Nickname = s.NickName,
                    Weight = s.Weight
                };
                msList.Add(micro);
            });
            return msList;
        }
        /// <summary>
        /// 健康检查，如果连接不上 每秒做一次尝试。
        /// 尝试 errorCount 次失败，软删除。
        /// 60次失败，永久删除。
        /// </summary>
        /// <param name="service">被检测主机信息</param>
        /// <param name="errorCount">尝试 errorCount 次失败，软删除</param>
        /// <returns></returns>
        public static void HealthCheck(ServiceInfo service, int errorCount = 3)
        {
            try
            {
                int hc = 60;//检查次数
                service.Checking = true;
                while (hc > 0)
                {
                    if (Alive(service))
                    {
                        if (hc == 60)
                        {
                            break;
                        }
                        else
                        {
                            WriteHealthCheck(service, hc, "恢复正常", ConsoleColor.DarkGreen);
                            if (service.IsTemporaryRemove)//如果服务已被临时移除则找回
                            {
                                lock (LockHelper) //防止高并发下 影响权重
                                {
                                    if (!Tc.ServiceInfoList.Exists(s => s.Ip == service.Ip && s.Port == service.Port))
                                    {
                                        //已存在不再添加 不存在则添加 
                                        for (int i = 0; i < service.Weight; i++)
                                        {
                                            Tc.ServiceInfoList.Add(service);
                                        }
                                    }
                                }
                                CheckNotice?.Invoke(service, NoticeType.RecoverHealth);
                            }
                        }
                        break;
                    }
                    else
                    {
                        hc--;
                        Log.WriteLine($"Error Info:{service.Ip}:{service.Port} not alive {hc}", ConsoleColor.DarkYellow);
                        if (hc == (60 - errorCount))//三次失败之后 临时移除 ，防止更多请求转发给此服务节点 
                        {
                            //临时移除 并不从配置文件移除
                            Tc.ServiceInfoList.RemoveAll(i => i.Ip == service.Ip && i.Port == service.Port);
                            service.IsTemporaryRemove = true;
                            CheckNotice?.Invoke(service, NoticeType.NotHealth);

                            WriteHealthCheck(service, hc, "故障恢复中", ConsoleColor.DarkYellow);
                        }
                        else if (hc == 0) //硬删除
                        {
                            Dictionary<string, string> rp = new Dictionary<string, string>
                        {
                            {"ip", service.Ip},
                            {"port", service.Port.ToString()}
                        };
                            Tc.Remove(rp);
                            CheckNotice?.Invoke(service, NoticeType.OffLine);

                            WriteHealthCheck(service, hc, "永久移除", ConsoleColor.DarkYellow);
                            break;
                        }
                        Thread.Sleep(1000); //间隔一秒 健康检查
                    }
                }
            }
            finally
            {
                service.Checking = false;
            }
        }

        private static bool Alive(ServiceInfo service)
        {
            bool isAlive = false;
            try
            {
                using (TTransport transport = new TSocket(service.Ip, service.Port, 5_000))
                {
                    transport.Open();
                    if (transport.IsOpen)
                    {
                        isAlive = true;
                        transport.Close();
                    }
                }
            }
            catch
            {
                isAlive = false;
            }
            return isAlive;
        }

        private static void WriteHealthCheck(ServiceInfo service, int hc, string msg, ConsoleColor consoleColor)
        {
            Log.WriteLine($"{service.Ip}:{service.Port}", consoleColor);
            foreach (var f in service.Name.Split(','))
            {
                Log.WriteLine($"{f}", consoleColor);
            }
            Log.WriteLine($"{"权重:" + service.Weight}", consoleColor);
            Log.WriteLine($"{msg}···{hc}！", consoleColor);
            Log.WriteLineNoDate($"-----------------------------------------------------------------------------");
        }
    }
}
