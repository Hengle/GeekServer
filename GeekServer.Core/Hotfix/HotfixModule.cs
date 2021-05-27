﻿using System;
using System.IO;
using System.Reflection;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Geek.Server
{
    public class HotfixModule
    {
        static readonly NLog.Logger LOGGER = NLog.LogManager.GetCurrentClassLogger();

        DllLoader dllLoader;
        internal Assembly HotfixAssembly { get; private set; }
        public IHotfixBridge HotfixBridge { get; private set; }

        //actor-actorAgent, comp-compAgent
        readonly Dictionary<Type, Type> agentTypeMap = new Dictionary<Type, Type>();
        readonly ConcurrentDictionary<object, IAgent> agentCacheMap = new ConcurrentDictionary<object, IAgent>();
        readonly ConcurrentDictionary<string, object> typeCacheMap = new ConcurrentDictionary<string, object>();

        //actorAgentType-list[listenerType]
        readonly ConcurrentDictionary<Type, List<Type>> evtTypeMap = new ConcurrentDictionary<Type, List<Type>>();
        readonly ConcurrentDictionary<Type, List<IEventListener>> evtCacheMap = new ConcurrentDictionary<Type, List<IEventListener>>();

        readonly Dictionary<int, Type> tcpHandlerMap = new Dictionary<int, Type>();
        readonly Dictionary<string, Type> httpHandlerMap = new Dictionary<string, Type>();

        public Task<bool> Load(string dllVersion, bool isReload)
        {
            bool success = false;
            try
            {
                bool writeDllVersion = false;
                string currentAssemblyDirectory = Environment.CurrentDirectory;
                string dllPath = Path.Combine(currentAssemblyDirectory, "GeekServer.Hotfix.dll");
                if (!string.IsNullOrEmpty(dllVersion))
                {
                    var path = Path.Combine(currentAssemblyDirectory, dllVersion + "/GeekServer.Hotfix.dll");
                    if (File.Exists(path))
                    {
                        dllPath = path;
                        writeDllVersion = true;
                    }
                    else
                    {
                        dllVersion = "org";
                    }
                }
                else
                {
                    dllVersion = "org";
                    var txtPath = Path.Combine(currentAssemblyDirectory, "dllVersion.txt");
                    if (File.Exists(txtPath))
                    {
                        var versionStr = File.ReadAllText(txtPath);
                        var path = Path.Combine(currentAssemblyDirectory, versionStr + "/GeekServer.Hotfix.dll");
                        if (File.Exists(path))
                        {
                            dllPath = path;
                            dllVersion = versionStr;
                        }
                    }
                }

                dllLoader = new DllLoader(dllPath);
                dllLoader.Load();
                HotfixAssembly = dllLoader.HotfixDll;

                if(!isReload)
                {
                    //依赖的dll
                    var asbArr = AppDomain.CurrentDomain.GetAssemblies();
                    var asbList = new List<string>();
                    foreach (var asb in asbArr)
                        asbList.Add(asb.GetName().Name);
                    var refArr = HotfixAssembly.GetReferencedAssemblies();
                    foreach(var asb in refArr)
                    {
                        if (asbList.Contains(asb.Name))
                            continue;
                        var refPath = Environment.CurrentDirectory + $"/{asb.Name}.dll";
                        if (File.Exists(refPath))
                            Assembly.LoadFrom(refPath);
                    }
                }

                loadDll();

                if (writeDllVersion)
                    File.WriteAllText(Path.Combine(currentAssemblyDirectory, "dllVersion.txt"), dllVersion);
                LOGGER.Info("hotfix dll loaded:" + dllVersion);
                success = true;
            }
            catch (Exception e)
            {
                if (!isReload)
                    throw e;
                LOGGER.Info("hotfix dll init failed..." + e.ToString());
            }
            return Task.FromResult(success);
        }

        public void Unload()
        {
            if (dllLoader != null)
            {
                var weak = dllLoader.Unload();
                if (Settings.Ins.IsDebug)
                {
                    //检查hotfix dll是否已经释放
                    Task.Run(async () =>
                    {
                        while (weak.IsAlive)
                        {
                            await Task.Delay(100);
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                        LOGGER.Warn("hotfix dll unloaded");
                    });
                }
            }
        }

        void loadDll()
        {
            var types = HotfixAssembly.GetTypes();
            foreach (var type in types)
            {
                addAgent(type);
                addEvent(type);
                addTcpHandler(type);
                addHttpHandler(type);
                if (HotfixBridge == null && type.GetInterface(typeof(IHotfixBridge).FullName) != null)
                {
                    var bridge = (IHotfixBridge)Activator.CreateInstance(type);
                    if (bridge.BridgeType == Settings.Ins.ServerType)
                        HotfixBridge = bridge;
                }
            }
        }

        void addAgent(Type type)
        {
            if (type.GetInterface(typeof(IAgent).FullName) == null)
                return;

            Type impType = type;
            while (impType != null && !impType.IsGenericType)
                impType = impType.BaseType;
            if (impType == null || !impType.IsGenericType)
                return;
            var argTypes = impType.GetGenericArguments();
            if (argTypes == null || argTypes.Length <= 0)
                return;
            agentTypeMap[argTypes[0]] = type;
        }

        void addEvent(Type type)
        {
            if (type.GetInterface(typeof(IEventListener).FullName) == null)
                return;

            if (!type.BaseType.IsGenericType)
                return;
            var argTypes = type.BaseType.GenericTypeArguments;
            if (argTypes.Length < 2)
                return;
            if (!evtTypeMap.ContainsKey(argTypes[0]))
                evtTypeMap.TryAdd(argTypes[0], new List<Type>());
            evtTypeMap[argTypes[0]].Add(type);
        }

        void addTcpHandler(Type type)
        {
            var attribute = (TcpMsgMapping)type.GetCustomAttribute(typeof(TcpMsgMapping), true);
            if (attribute == null) return;
            var msgIdField = attribute.Msg.GetField("MsgId", BindingFlags.Static | BindingFlags.Public);
            if (msgIdField == null) return;
            int msgId = (int)msgIdField.GetValue(null);
            if (!tcpHandlerMap.ContainsKey(msgId))
            {
                tcpHandlerMap.Add(msgId, type);
            }
            else
            {
                LOGGER.Error("重复注册消息tcp handler:[{}] msg:[{}]", msgId, type);
            }
        }

        void addHttpHandler(Type type)
        {
            var attribute = (HttpMsgMapping)type.GetCustomAttribute(typeof(HttpMsgMapping), true);
            if (attribute == null) return;
            var cmd = attribute.cmd;
            if (httpHandlerMap.ContainsKey(cmd))
                LOGGER.Warn($"http cmd handler 已存在：{cmd}，新的handler将覆盖老的handler");
            httpHandlerMap[cmd] = type;
        }

        public T GetHandler<T>(int msgId)
        {
            if (!tcpHandlerMap.ContainsKey(msgId))
            {
                LOGGER.Error("未注册的 handler 消息ID:{}", msgId);
                return default;
            }

            Type handlerType = tcpHandlerMap[msgId];
            var handler = Activator.CreateInstance(handlerType);
            if (handler == null)
                LOGGER.Error("创建 handler失败:{} {}", msgId, handlerType.ToString());
            return (T)handler;
        }

        public BaseHttpHandler GetHttpHandler(string cmd)
        {
            if (!httpHandlerMap.ContainsKey(cmd))
            {
                LOGGER.Error("未注册的http消息:{}", cmd);
                return null;
            }
            Type msgType = httpHandlerMap[cmd];
            var msg = Activator.CreateInstance(msgType) as BaseHttpHandler;
            if (msg == null)
                LOGGER.Error("http handler创建失败:{} {}", cmd, msgType.ToString());
            return msg;
        }

        public bool IsTypeFromHotfix(Type type)
        {
            if (type == null)
                return false;
            if (type.IsGenericType)
            {
                var argTypes = type.GetGenericArguments();
                foreach (var inType in argTypes)
                {
                    if (IsTypeFromHotfix(inType))
                        return true;
                }
            }
            if (type.BaseType != null)
                return IsTypeFromHotfix(type.BaseType);
            return type.Assembly == HotfixAssembly;
        }

        /// <summary>
        /// 对应agent是不是对应接口
        /// </summary>
        public bool IsAgentInterface(Type refType, Type interfaceType)
        {
            if (!agentTypeMap.ContainsKey(refType))
                return false;
            var type = agentTypeMap[refType];
            return type.GetInterface(interfaceType.FullName) != null;
        }

        /// <summary>
        /// 获取热更代理实例
        /// </summary>
        public T GetAgent<T>(object refOwner) where T : IAgent
        {
            if (agentCacheMap.TryGetValue(refOwner, out var cache))
                return (T)cache;

            if (!agentTypeMap.ContainsKey(refOwner.GetType()))
                return default;

            lock (agentCacheMap)
            {
                if (agentCacheMap.ContainsKey(refOwner))
                    return (T)agentCacheMap[refOwner];

                var agent = (T)Activator.CreateInstance(agentTypeMap[refOwner.GetType()]);
                agent.Owner = refOwner;
                agentCacheMap.TryAdd(refOwner, agent);
                return agent;
            }
        }

        /// <summary>
        /// 移除cacheAgent
        /// </summary>
        public void RemoveAgentCache(object refOwner)
        {
            agentCacheMap.TryRemove(refOwner, out var _);
        }

        public List<IEventListener> GetEventListeners(Type actorAgentType)
        {
            evtCacheMap.TryGetValue(actorAgentType, out var list);
            if (list != null)
                return list;

            if (!evtTypeMap.ContainsKey(actorAgentType))
                return default;

            lock (actorAgentType)
            {
                evtCacheMap.TryGetValue(actorAgentType, out list);
                if (list != null)
                    return list;

                list = new List<IEventListener>();
                foreach (var type in evtTypeMap[actorAgentType])
                {
                    var listener = (IEventListener)Activator.CreateInstance(type);
                    list.Add(listener);
                }
                evtCacheMap[actorAgentType] = list;
            }
            return list;
        }

        /// <summary>获取实例(主要用于获取Event,Timer, Schedule,的Handler实例)</summary>
        public T GetInstance<T>(string typeName)
        {
            typeCacheMap.TryGetValue(typeName, out var cacheObj);
            if (cacheObj != null)
                return (T)cacheObj;

            var obj = CreateInstance<T>(typeName);
            typeCacheMap.TryAdd(typeName, obj);
            return obj;
        }

        public Type GetType(string typeName)
        {
            return HotfixAssembly.GetType(typeName);
        }

        public T CreateInstance<T>(string typeName)
        {
            return (T)HotfixAssembly.CreateInstance(typeName);
        }
    }
}