﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO;
using System.Threading.Tasks;
using Anno.Rpc;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;

namespace Anno.Rpc.Center
{
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
            List<Micro> msList = new List<Micro>();
            List<ServiceInfo> service = Tc.ServiceInfoList.FindAll(i => i.Name.Contains(channel));
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
            int hc = 60;//检查次数

        hCheck://再次  心跳检测
            var client = new BrokerService.BrokerServiceClient(new Channel($"{service.Ip}:{service.Port}", ChannelCredentials.Insecure));
            try
            {
                service.Checking = true;
                if (!client.Ping(new Empty()).Reply)
                {
                    if (hc != 60)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: ");
                        Console.WriteLine($"{service.Ip}:{service.Port}");
                        Console.WriteLine($"{service.Name}");
                        foreach (var f in service.Name.Split(','))
                        {
                            Console.WriteLine($"{f}");
                        }
                        Console.WriteLine($"{"w:" + service.Weight}");
                        Console.WriteLine($"恢复正常！");
                        Console.ResetColor();
                        Console.WriteLine($"----------------------------------------------------------------- ");
                    }
                    if (hc <= (60 - errorCount))
                    {
                        CheckNotice?.Invoke(service, NoticeType.RecoverHealth);
                    }
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
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Info:{service.Ip}:{service.Port} {ex.Message}");
                if (hc == 60)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: ");
                    Console.WriteLine($"{service.Ip}:{service.Port}");
                    foreach (var f in service.Name.Split(','))
                    {
                        Console.WriteLine($"{f}");
                    }
                    Console.WriteLine($"{"w:" + service.Weight}");
                    Console.WriteLine($"检测中···{hc}！");
                    Console.ResetColor();
                    Console.WriteLine($"----------------------------------------------------------------- ");
                }
                else if (hc == (60 - errorCount))
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: ");
                    Console.WriteLine($"{service.Ip}:{service.Port}");
                    foreach (var f in service.Name.Split(','))
                    {
                        Console.WriteLine($"{f}");
                    }
                    Console.WriteLine($"{"w:" + service.Weight}");
                    Console.WriteLine($"故障恢复中···{hc}！");
                    Console.ResetColor();
                    Console.WriteLine($"----------------------------------------------------------------- ");
                }
                else if (hc == 0) //硬删除
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}:");
                    Console.WriteLine($"{service.Ip}:{service.Port}");
                    foreach (var f in service.Name.Split(','))
                    {
                        Console.WriteLine($"{f}");
                    }
                    Console.WriteLine($"{"w:" + service.Weight}");
                    Console.WriteLine($"永久移除！");
                    Console.ResetColor();
                    Console.WriteLine($"----------------------------------------------------------------- ");
                    CheckNotice?.Invoke(service, NoticeType.OffLine);
                }

                if (hc == (60 - errorCount)) //三次失败之后 临时移除 ，防止更多请求转发给此服务节点 
                {
                    //临时移除 并不从配置文件移除
                    Tc.ServiceInfoList.RemoveAll(i => i.Ip == service.Ip && i.Port == service.Port);
                    CheckNotice?.Invoke(service, NoticeType.NotHealth);
                }
                else if (hc == 0) //硬删除
                {
                    Dictionary<string, string> rp = new Dictionary<string, string>
                    {
                        {"ip", service.Ip},
                        {"port", service.Port.ToString()}
                    };
                    Tc.Remove(rp);
                    return;
                }

                Thread.Sleep(1000); //间隔一秒 健康检查
                hc--;
                goto hCheck;
            }
            finally
            {
                service.Checking = false;
            }
        }
    }
}
