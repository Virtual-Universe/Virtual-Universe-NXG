/* 13 March 2019
 * 
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;

using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Monitoring;
using OpenSim.Region.ClientStack.LindenUDP;
using OpenSim.Region.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Connectors.Hypergrid;

using OpenMetaverse;
using OpenMetaverse.Packets;
using log4net;
using Nini.Config;
using Mono.Addins;

using DirFindFlags = OpenMetaverse.DirectoryManager.DirFindFlags;

namespace OpenSim.Region.CoreModules.Framework.UserManagement
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "UserManagementModule")]
    public class UserManagementModule : ISharedRegionModule, IUserManagement, IPeople
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected bool m_Enabled;
        protected List<Scene> m_Scenes = new List<Scene>();

        // The cache. Static so one cache for all scenes.
        protected static Dictionary<UUID, UserData> m_UserCache = new Dictionary<UUID, UserData>();

        protected bool m_DisplayChangingHomeURI = false;

        private static readonly NActionChain m_queue = new NActionChain(8);

        #region ISharedRegionModule

        public virtual void Initialise(IConfigSource config)
        {
            string umanmod = config.Configs["Modules"].GetString("UserManagementModule", Name);
            if (umanmod == Name)
            {
                m_Enabled = true;
                Init();
                m_log.DebugFormat("[USER MANAGEMENT MODULE]: {0} is enabled", Name);
            }

            if(!m_Enabled)
            {
                return;
            }

            IConfig userManagementConfig = config.Configs["UserManagement"];
            if (userManagementConfig == null)
                return;

            m_DisplayChangingHomeURI = userManagementConfig.GetBoolean("DisplayChangingHomeURI", false);
        }

        public virtual bool IsSharedModule
        {
            get { return true; }
        }

        public virtual string Name
        {
            get { return "BasicUserManagementModule"; }
        }

        public virtual Type ReplaceableInterface
        {
            get { return null; }
        }

        public virtual void AddRegion(Scene scene)
        {
            if (m_Enabled)
            {
                lock (m_Scenes)
                {
                    m_Scenes.Add(scene);
                }

                scene.RegisterModuleInterface<IUserManagement>(this);
                scene.RegisterModuleInterface<IPeople>(this);
                scene.EventManager.OnNewClient += new EventManager.OnNewClientDelegate(EventManager_OnNewClient);
                scene.EventManager.OnPrimsLoaded += new EventManager.PrimsLoaded(EventManager_OnPrimsLoaded);
            }
        }

        public virtual void RemoveRegion(Scene scene)
        {
            if (m_Enabled)
            {
                scene.UnregisterModuleInterface<IUserManagement>(this);
                lock (m_Scenes)
                {
                    m_Scenes.Remove(scene);
                }
            }
        }

        public virtual void RegionLoaded(Scene s)
        {
        }

        public virtual void PostInitialise()
        {
        }

        public virtual void Close()
        {
            lock (m_Scenes)
                m_Scenes.Clear();

            lock (m_UserCache)
                m_UserCache.Clear();
        }

        #endregion ISharedRegionModule

        #region Event Handlers

        
        protected virtual void EventManager_OnPrimsLoaded(Scene s)
        {
            // let's sniff all the user names referenced by objects in the scene

            // Nani: OR... let's NOT do this since it can take AGES on a big sim?
            // User names will get resolved when they are requested. 
            // OK lets do it anyway but I hate it, so we do it in a thread and skip
            // checking the same UUID multiple times in a row.
            Util.FireAndForget(
                 delegate
                 {
                     s.ForEachSOG(
                         delegate (SceneObjectGroup sog)
                         {
                             try
                             {
                                 UUID prevID = sog.RootPart.CreatorID;
                                 AddUser(sog.RootPart.CreatorID, sog.RootPart.CreatorData);
                                 foreach (SceneObjectPart sop in sog.Parts)
                                 {
                                     if (sop.CreatorID != prevID)
                                     {
                                         AddUser(sop.CreatorID, sop.CreatorData);
                                         prevID = sop.CreatorID;
                                     }

                                     foreach (TaskInventoryItem item in sop.TaskInventory.Values)
                                     {
                                         if (item.CreatorID == prevID)
                                             continue;

                                         AddUser(item.CreatorID, item.CreatorData);
                                         prevID = item.CreatorID;
                                     }
                                 }
                             }
                             catch { }
                         });
                 }, null, "OnPrimsLoaded.CacheCreators", false);
        }

        protected virtual void EventManager_OnNewClient(IClientAPI client)
        {
            client.OnConnectionClosed += new Action<IClientAPI>(HandleConnectionClosed);
            client.OnNameFromUUIDRequest += new UUIDNameRequest(HandleUUIDNameRequest);
            client.OnAvatarPickerRequest += new AvatarPickerRequest(HandleAvatarPickerRequest);
        }

        protected virtual void HandleConnectionClosed(IClientAPI client)
        {
            client.OnNameFromUUIDRequest -= new UUIDNameRequest(HandleUUIDNameRequest);
            client.OnAvatarPickerRequest -= new AvatarPickerRequest(HandleAvatarPickerRequest);
            client.OnConnectionClosed -= new Action<IClientAPI>(HandleConnectionClosed);
        }

        protected virtual void HandleUUIDNameRequest(UUID uuid, IClientAPI client)
        {
            if (m_Scenes.Count <= 0)
                return;

            try
            {
                if (!client.IsActive)
                    return;

                if (m_Scenes[0].LibraryService != null && (m_Scenes[0].LibraryService.LibraryRootFolder.Owner == uuid))
                {
                    client.SendNameReply(uuid, "Mr", "OpenSim");
                    return;
                }

                UserData user;

                bool found = false;
                lock (m_UserCache)
                      found = m_UserCache.TryGetValue(uuid, out user);

                if (found)
                {
                    if (!user.IsUnknownUser && user.HasGridUserTried)
                    {
                        if (client.IsActive)
                            client.SendNameReply(uuid, user.FirstName, user.LastName);
                        return;
                    }
                }
            }
            catch
            {
                return;
            }

            // Not found in cache, queue continuation
            m_queue.Enqueue(delegate
                   {
                       UserDataRequest(client, uuid);
                   });           
        }

        public virtual void HandleAvatarPickerRequest(IClientAPI client, UUID avatarID, UUID RequestID, string query)
        {
            //EventManager.TriggerAvatarPickerRequest();

            m_log.DebugFormat("[USER MANAGEMENT MODULE]: HandleAvatarPickerRequest for {0}", query);

            try
            {
                List<UserData> users = GetUserData(query, 500, 1);

                AvatarPickerReplyPacket replyPacket = (AvatarPickerReplyPacket)PacketPool.Instance.GetPacket(PacketType.AvatarPickerReply);
                // TODO: don't create new blocks if recycling an old packet

                AvatarPickerReplyPacket.DataBlock[] searchData =
                    new AvatarPickerReplyPacket.DataBlock[users.Count];
                AvatarPickerReplyPacket.AgentDataBlock agentData = new AvatarPickerReplyPacket.AgentDataBlock();

                agentData.AgentID = avatarID;
                agentData.QueryID = RequestID;
                replyPacket.AgentData = agentData;
                //byte[] bytes = new byte[AvatarResponses.Count*32];

                int i = 0;
                foreach (UserData item in users)
                {
                    UUID translatedIDtem = item.Id;
                    searchData[i] = new AvatarPickerReplyPacket.DataBlock();
                    searchData[i].AvatarID = translatedIDtem;
                    searchData[i].FirstName = Utils.StringToBytes((string)item.FirstName);
                    searchData[i].LastName = Utils.StringToBytes((string)item.LastName);
                    i++;
                }

                if (users.Count == 0)
                {
                    searchData = new AvatarPickerReplyPacket.DataBlock[0];
                }
                replyPacket.Data = searchData;

                AvatarPickerReplyAgentDataArgs agent_data = new AvatarPickerReplyAgentDataArgs();
                agent_data.AgentID = replyPacket.AgentData.AgentID;
                agent_data.QueryID = replyPacket.AgentData.QueryID;

                List<AvatarPickerReplyDataArgs> data_args = new List<AvatarPickerReplyDataArgs>();
                for (i = 0; i < replyPacket.Data.Length; i++)
                {
                    AvatarPickerReplyDataArgs data_arg = new AvatarPickerReplyDataArgs();
                    data_arg.AvatarID = replyPacket.Data[i].AvatarID;
                    data_arg.FirstName = replyPacket.Data[i].FirstName;
                    data_arg.LastName = replyPacket.Data[i].LastName;
                    data_args.Add(data_arg);
                }
                client.SendAvatarPickerReply(agent_data, data_args);
            }
            catch
            {
                return;
            }
        }

        protected virtual void AddAdditionalUsers(string query, List<UserData> users)
        {
        }

        #endregion Event Handlers

        #region IPeople

        public virtual List<UserData> GetUserData(string query, int page_size, int page_number)
        {
            if(m_Scenes.Count <= 0)
                return new List<UserData>();

            List<UserData> users = new List<UserData>();

            try
            {
                // search the user accounts service
                List<UserAccount> accs = m_Scenes[0].UserAccountService.GetUserAccounts(m_Scenes[0].RegionInfo.ScopeID, query);

                if (accs != null)
                {
                    foreach (UserAccount acc in accs)
                    {
                        UserData ud = new UserData();
                        ud.FirstName = acc.FirstName;
                        ud.LastName = acc.LastName;
                        ud.Id = acc.PrincipalID;
                        ud.HasGridUserTried = true;
                        ud.IsUnknownUser = false;
                        users.Add(ud);
                    }
                }

                // search the local cache
                foreach (UserData data in m_UserCache.Values)
                {
                    if (data.Id != UUID.Zero && !data.IsUnknownUser &&
                        users.Find(delegate (UserData d) { return d.Id == data.Id; }) == null &&
                        (data.FirstName.ToLower().StartsWith(query.ToLower()) || data.LastName.ToLower().StartsWith(query.ToLower())))
                    {
                        users.Add(data);
                    }
                }

                AddAdditionalUsers(query, users);
            }
            catch { }

            return users;
        }

        #endregion IPeople

        /// <summary>
        ///
        /// </summary>
        /// <param name="uuid"></param>
        /// <param name="names">Caller please provide a properly instantiated array for names, string[2]</param>
        /// <returns></returns>
        protected virtual bool TryGetUserNames(UUID uuid, string[] names)
        {
            if (names == null)
                names = new string[2];

            if (TryGetUserNamesFromCache(uuid, names))
                return true;

            if (TryGetUserNamesFromServices(uuid, names))
                return true;

            return false;
        }

        protected virtual bool TryGetUserNamesFromCache(UUID uuid, string[] names)
        {
            lock (m_UserCache)
            {
                if (m_UserCache.ContainsKey(uuid))
                {
                    names[0] = m_UserCache[uuid].FirstName;
                    names[1] = m_UserCache[uuid].LastName;

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Try to get the names bound to the given uuid, from the services.
        /// </summary>
        /// <returns>True if the name was found, false if not.</returns>
        /// <param name='uuid'></param>
        /// <param name='names'>The array of names if found.  If not found, then names[0] = "Unknown" and names[1] = "User"</param>
        protected virtual bool TryGetUserNamesFromServices(UUID uuid, string[] names)
        {
            if(m_Scenes.Count <= 0)
                return false;

            UserAccount account = m_Scenes[0].UserAccountService.GetUserAccount(UUID.Zero, uuid);

            if (account != null)
            {
                names[0] = account.FirstName;
                names[1] = account.LastName;

                UserData user = new UserData();
                user.FirstName = account.FirstName;
                user.LastName = account.LastName;

                lock (m_UserCache)
                      m_UserCache[uuid] = user;

                return true;
            }
            else
            {
                // Let's try the GridUser service
                GridUserInfo uInfo = m_Scenes[0].GridUserService.GetGridUserInfo(uuid.ToString());
                if (uInfo != null)
                {
                    string url, first, last, tmp;
                    UUID u;
                    if (Util.ParseUniversalUserIdentifier(uInfo.UserID, out u, out url, out first, out last, out tmp))
                    {
                        AddUser(uuid, first, last, url);

                        if (m_UserCache.ContainsKey(uuid))
                        {
                            names[0] = m_UserCache[uuid].FirstName;
                            names[1] = m_UserCache[uuid].LastName;

                            return true;
                        }
                    }
                    else
                        m_log.DebugFormat("[USER MANAGEMENT MODULE]: Unable to parse UUI {0}", uInfo.UserID);
                }

                names[0] = "Unknown";
                names[1] = "UserUMMTGUN9";

                return false;
            }
        }

        #region IUserManagement

        public virtual UUID GetUserIdByName(string name)
        {
            string[] parts = name.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                throw new Exception("Name must have 2 components");

            return GetUserIdByName(parts[0], parts[1]);
        }

        public virtual UUID GetUserIdByName(string firstName, string lastName)
        {
            if(m_Scenes.Count <= 0)
                return UUID.Zero;

            // TODO: Optimize for reverse lookup if this gets used by non-console commands.
            lock (m_UserCache)
            {
                foreach (UserData user in m_UserCache.Values)
                {
                    if (user.FirstName == firstName && user.LastName == lastName)
                        return user.Id;
                }
            }

            try
            {
                UserAccount account = m_Scenes[0].UserAccountService.GetUserAccount(UUID.Zero, firstName, lastName);

                if (account != null)
                    return account.PrincipalID;
            }
            catch { }

            return UUID.Zero;
        }

        public virtual string GetUserName(UUID uuid)
        {
            UserData user;
            GetUser(uuid, out user);
            return user.FirstName + " " + user.LastName;
        }

        public virtual Dictionary<UUID,string> GetUsersNames(string[] ids)
        {
            Dictionary<UUID,string> ret = new Dictionary<UUID,string>();
            if(m_Scenes.Count <= 0)
                return ret;

            List<string> missing = new List<string>();
            Dictionary<UUID,string> untried = new Dictionary<UUID, string>();

            // look in cache
            UserData userdata = new UserData();
            UUID uuid = UUID.Zero;

            int nids = ids.Length;
            for(int i=0; i<nids; i++)
            {
                if(UUID.TryParse(ids[i], out uuid))
                {
                    bool found = false;
                    lock (m_UserCache)
                        found = m_UserCache.TryGetValue(uuid, out userdata);
                            
                    if (found  &&
                        userdata.FirstName != "Unknown" && userdata.FirstName != string.Empty)
                    {
                        string name = userdata.FirstName + " " + userdata.LastName;

                        if (userdata.HasGridUserTried)
                            ret[uuid] = name;
                        else
                        {
                            untried[uuid] = name;
                            missing.Add(ids[i]);
                        }
                    }
                    else
                       missing.Add(ids[i]);
                }
            }

            if(missing.Count == 0)
                return ret;

            try
            {
                // try user account service
                List<UserAccount> accounts = m_Scenes[0].UserAccountService.GetUserAccounts(
                                             m_Scenes[0].RegionInfo.ScopeID, missing);

                if (accounts.Count != 0)
                {
                    foreach (UserAccount uac in accounts)
                    {
                        if (uac != null)
                        {
                            string name = uac.FirstName + " " + uac.LastName;
                            ret[uac.PrincipalID] = name;
                            missing.Remove(uac.PrincipalID.ToString()); // slowww
                            untried.Remove(uac.PrincipalID);

                            userdata = new UserData();
                            userdata.Id = uac.PrincipalID;
                            userdata.FirstName = uac.FirstName;
                            userdata.LastName = uac.LastName;
                            userdata.HomeURL = string.Empty;
                            userdata.IsUnknownUser = false;
                            userdata.HasGridUserTried = true;
                            lock (m_UserCache)
                                  m_UserCache[uac.PrincipalID] = userdata;
                        }
                    }
                }

                if (missing.Count == 0 || m_Scenes[0].GridUserService == null)
                    return ret;

                // try grid user service

                GridUserInfo[] pinfos = m_Scenes[0].GridUserService.GetGridUserInfo(missing.ToArray());
                int npinfos = pinfos.Length;
                for (int i = 0; i < npinfos; i++)
                {
                    if (pinfos[i] != null)
                    {
                        string url, first, last, tmp;

                        if (pinfos[i].UserID.Length <= 36)
                            continue;

                        if (Util.ParseUniversalUserIdentifier(pinfos[i].UserID, out uuid, out url, out first, out last, out tmp))
                        {
                            if (url != string.Empty)
                            {
                                try
                                {
                                    userdata = new UserData();
                                    userdata.FirstName = first.Replace(" ", ".") + "." + last.Replace(" ", ".");
                                    userdata.LastName = "@" + new Uri(url).Authority;
                                    userdata.Id = uuid;
                                    userdata.HomeURL = url;
                                    userdata.IsUnknownUser = false;
                                    userdata.HasGridUserTried = true;
                                    lock (m_UserCache)
                                          m_UserCache[uuid] = userdata;

                                    string name = userdata.FirstName + " " + userdata.LastName;
                                    ret[uuid] = name;
                                    missing.Remove(uuid.ToString());
                                    untried.Remove(uuid);
                                }
                                catch { }
                            }
                        }
                    }
                }                

                // add the untried in cache that still failed
                if (untried.Count > 0)
                {
                    foreach (KeyValuePair<UUID, string> kvp in untried)
                    {
                        ret[kvp.Key] = kvp.Value;
                        missing.Remove((kvp.Key).ToString());
                    }
                }

                // add the UMMthings ( not sure we should)
                if (missing.Count > 0)
                {
                    foreach (string id in missing)
                    {
                        if (UUID.TryParse(id, out uuid) && uuid != UUID.Zero)
                        {
                            if (m_Scenes[0].LibraryService != null &&
                                 (m_Scenes[0].LibraryService.LibraryRootFolder.Owner == uuid))
                                ret[uuid] = "Mr OpenSim";
                            else
                                ret[uuid] = "Unknown UserUMMAU43";
                        }
                    }
                }
            }
            catch { }

            return ret;
        }

        public virtual string GetUserHomeURL(UUID userID)
        {
            UserData user;
            if(GetUser(userID, out user))
            {
                return user.HomeURL;
            }
            return string.Empty;
        }

        public virtual string GetUserServerURL(UUID userID, string serverType)
        {
            UserData userdata;
            if (!GetUser(userID, out userdata))
            {
                return string.Empty;
            }

            if (userdata.ServerURLs != null && userdata.ServerURLs.ContainsKey(serverType) && userdata.ServerURLs[serverType] != null)
            {
                return userdata.ServerURLs[serverType].ToString();
            }

            if (!string.IsNullOrEmpty(userdata.HomeURL))
            {
                UserAgentServiceConnector uConn = new UserAgentServiceConnector(userdata.HomeURL);
                try
                {
                    userdata.ServerURLs = uConn.GetServerURLs(userID);
                }
                catch(System.Net.WebException e)
                {
                    m_log.DebugFormat("[USER MANAGEMENT MODULE]: GetServerURLs call failed {0}", e.Message);
                    userdata.ServerURLs = new Dictionary<string, object>();
                }
                catch (Exception e)
                {
                    m_log.Debug("[USER MANAGEMENT MODULE]: GetServerURLs call failed ", e);
                    userdata.ServerURLs = new Dictionary<string, object>();
                }

                if (userdata.ServerURLs != null && userdata.ServerURLs.ContainsKey(serverType) && userdata.ServerURLs[serverType] != null)
                    return userdata.ServerURLs[serverType].ToString();
            }
            return string.Empty;
        }

        public virtual string GetUserUUI(UUID userID)
        {
            string uui;
            GetUserUUI(userID, out uui);
            return uui;
        }

        public virtual bool GetUserUUI(UUID userID, out string uui)
        {
            UserData ud;
            bool result = GetUser(userID, out ud);

            if (ud != null)
            {
                string homeURL = ud.HomeURL;
                string first = ud.FirstName, last = ud.LastName;
                if (ud.LastName.StartsWith("@"))
                {
                    string[] parts = ud.FirstName.Split('.');
                    if (parts.Length >= 2)
                    {
                        first = parts[0];
                        last = parts[1];
                    }
                    uui = userID + ";" + homeURL + ";" + first + " " + last;
                    return result;
                }
            }

            uui = userID.ToString();
            return result;
        }

        #region Cache Management
        public virtual bool GetUser(UUID uuid, out UserData userdata)
        {
            if (m_Scenes.Count <= 0)
            {
                userdata = new UserData();
                return false;
            }

            bool found = false;
            lock (m_UserCache)
                  found = m_UserCache.TryGetValue(uuid, out userdata);

            if (found)
            {
                if (userdata.HasGridUserTried)
                {
                    return true;
                }
            }
            else
            {
                userdata = new UserData();
                userdata.Id = uuid;
                userdata.FirstName = "Unknown";
                userdata.LastName = "UserUMMAU42";
                userdata.HomeURL = string.Empty;
                userdata.IsUnknownUser = true;
                userdata.HasGridUserTried = false;
            }

            /* BEGIN: do not wrap this code in any lock here
             * There are HTTP calls in here.
             */
            try
            {
                if (!userdata.HasGridUserTried)
                {
                    /* rewrite here */
                    UserAccount account = m_Scenes[0].UserAccountService.GetUserAccount(m_Scenes[0].RegionInfo.ScopeID, uuid);
                    if (account != null)
                    {
                        userdata.FirstName = account.FirstName;
                        userdata.LastName = account.LastName;
                        userdata.HomeURL = string.Empty;
                        userdata.IsUnknownUser = false;
                        userdata.HasGridUserTried = true;
                    }
                }

                if (!userdata.HasGridUserTried)
                {
                    GridUserInfo uInfo = null;
                    if (null != m_Scenes[0].GridUserService)
                    {
                        uInfo = m_Scenes[0].GridUserService.GetGridUserInfo(uuid.ToString());
                    }
                    if (uInfo != null)
                    {
                        string url, first, last, tmp;
                        UUID u;
                        if (uInfo.UserID.Length <= 36)
                        {
                            /* not a UUI */
                        }
                        else if (Util.ParseUniversalUserIdentifier(uInfo.UserID, out u, out url, out first, out last, out tmp))
                        {
                            if (url != string.Empty)
                            {
                                userdata.FirstName = first.Replace(" ", ".") + "." + last.Replace(" ", ".");
                                userdata.HomeURL = url;
                                try
                                {
                                    userdata.LastName = "@" + new Uri(url).Authority;
                                    userdata.IsUnknownUser = false;
                                }
                                catch
                                {
                                    userdata.LastName = "@unknown";
                                }
                                userdata.HasGridUserTried = true;
                            }
                        }
                        else
                            m_log.DebugFormat("[USER MANAGEMENT MODULE]: Unable to parse UUI {0}", uInfo.UserID);
                    }
                }
            }
            catch
            {
                userdata = new UserData();
                userdata.Id = uuid;
                userdata.FirstName = "Unknown";
                userdata.LastName = "UserUMMAU42";
                userdata.HomeURL = string.Empty;
                userdata.IsUnknownUser = true;
                userdata.HasGridUserTried = false;

                // Do not cache this, just get out.
                return !userdata.IsUnknownUser;
            }

            /* END: do not wrap this code in any lock here */

            lock (m_UserCache)
                  m_UserCache[uuid] = userdata;

            return !userdata.IsUnknownUser;
        }

        public virtual void AddUser(UUID uuid, string first, string last, bool isNPC = false)
        {
            lock(m_UserCache)
            {
                if(!m_UserCache.ContainsKey(uuid))
                {
                    UserData user = new UserData();
                    user.Id = uuid;
                    user.FirstName = first;
                    user.LastName = last;
                    user.IsUnknownUser = false;
                    user.HasGridUserTried = isNPC;
                    m_UserCache.Add(uuid, user);
                }
            }
        }

        public virtual void AddUser(UUID uuid, string first, string last, string homeURL)
        {
            //m_log.DebugFormat("[USER MANAGEMENT MODULE]: Adding user with id {0}, first {1}, last {2}, url {3}", uuid, first, last, homeURL);
            bool found = false;

            UserData oldUser;
            lock (m_UserCache)
                  found = m_UserCache.TryGetValue(uuid, out oldUser);
            
            if (found)
            {
                if (!oldUser.IsUnknownUser)
                {
                    if (homeURL != oldUser.HomeURL && m_DisplayChangingHomeURI)
                    {
                        m_log.DebugFormat("[USER MANAGEMENT MODULE]: Different HomeURI for {0} {1} ({2}): {3} and {4}",
                                first, last, uuid.ToString(), homeURL, oldUser.HomeURL);
                    }
                    /* no update needed */
                    return;
                }
            }
            else // Not found!
            {
                oldUser = new UserData();
                oldUser.HasGridUserTried = false;
                oldUser.IsUnknownUser = false;
                if (homeURL != string.Empty)
                {
                    oldUser.FirstName = first.Replace(" ", ".") + "." + last.Replace(" ", ".");
                    try
                    {
                        oldUser.LastName = "@" + new Uri(homeURL).Authority;
                        oldUser.IsUnknownUser = false;
                    }
                    catch
                    {
                        oldUser.LastName = "@unknown";
                    }
                }
                else
                {
                    oldUser.FirstName = first;
                    oldUser.LastName = last;
                }
                oldUser.HomeURL = homeURL;
                oldUser.Id = uuid;

                lock (m_UserCache)
                      m_UserCache[uuid] = oldUser;
            }
        }

        public virtual void AddUser(UUID id, string creatorData)
        {
            // m_log.InfoFormat("[USER MANAGEMENT MODULE]: Adding user with id {0}, creatorData {1}", id, creatorData);

            if (string.IsNullOrEmpty(creatorData))
            {
                AddUser(id, "Unknown", "UserUMMAU6", string.Empty);
            }
            else
            {
                string homeURL;
                string firstname = string.Empty;
                string lastname = string.Empty;

                //creatorData = <endpoint>;<name>

                string[] parts = creatorData.Split(';');
                if(parts.Length > 1)
                {
                    string[] nameparts = parts[1].Split(' ');
                    firstname = nameparts[0];
                    for(int xi = 1; xi < nameparts.Length; ++xi)
                    {
                        if(xi != 1)
                        {
                            lastname += " ";
                        }
                        lastname += nameparts[xi];
                    }
                }
                else
                {
                    firstname = "Unknown";
                    lastname = "UserUMMAU5";
                }

                if (parts.Length >= 1)
                {
                    homeURL = parts[0];
                    if (Uri.IsWellFormedUriString(homeURL, UriKind.Absolute))
                    {
                        AddUser(id, firstname, lastname, homeURL);
                    }
                    else
                    {
                        m_log.DebugFormat("[SCENE]: Unable to parse Uri {0} for CreatorID {1}", parts[0], creatorData);

                        lock (m_UserCache)
                        {
                            if (!m_UserCache.ContainsKey(id))
                            {
                                UserData newUser = new UserData();
                                newUser.Id = id;
                                newUser.FirstName = firstname + "." + lastname.Replace(' ', '.');
                                newUser.LastName = "@unknown";
                                newUser.HomeURL = string.Empty;
                                newUser.HasGridUserTried = false;
                                newUser.IsUnknownUser = true; /* we mark those users as Unknown user so a re-retrieve may be activated */
                                m_UserCache.Add(id, newUser);
                            }
                        }
                    }
                }
                else
                {
                    lock(m_UserCache)
                    {
                        if(!m_UserCache.ContainsKey(id))
                        {
                            UserData newUser = new UserData();
                            newUser.Id = id;
                            newUser.FirstName = "Unknown";
                            newUser.LastName = "UserUMMAU4";
                            newUser.HomeURL = string.Empty;
                            newUser.IsUnknownUser = true;
                            newUser.HasGridUserTried = false;
                            m_UserCache.Add(id, newUser);
                        }
                    }
                }
            }
        }
        #endregion

        public virtual bool IsLocalGridUser(UUID uuid)
        {
            lock (m_Scenes)
            {
                if (m_Scenes.Count == 0)
                    return true;

                try
                {
                    UserAccount account = m_Scenes[0].UserAccountService.GetUserAccount(m_Scenes[0].RegionInfo.ScopeID, uuid);
                    if (account == null || (account != null && !account.LocalToGrid))
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        #endregion IUserManagement

        protected virtual void Init()
        {
            AddUser(UUID.Zero, "Unknown", "User");
            RegisterConsoleCmds();
        }

        protected virtual void RegisterConsoleCmds()
        {
            MainConsole.Instance.Commands.AddCommand("Users", true,
                "show name",
                "show name <uuid>",
                "Show the bindings between a single user UUID and a user name",
                String.Empty,
                HandleShowUser);

            MainConsole.Instance.Commands.AddCommand("Users", true,
                "show names",
                "show names",
                "Show the bindings between user UUIDs and user names",
                String.Empty,
                HandleShowUsers);

            MainConsole.Instance.Commands.AddCommand("Users", true,
                "reset user cache",
                "reset user cache",
                "reset user cache to allow changed settings to be applied",
                String.Empty,
                HandleResetUserCache);
        }

        protected virtual void HandleResetUserCache(string module, string[] cmd)
        {
            lock(m_UserCache)
            {
                m_UserCache.Clear();
            }
        }

        protected virtual void HandleShowUser(string module, string[] cmd)
        {
            if (cmd.Length < 3)
            {
                MainConsole.Instance.OutputFormat("Usage: show name <uuid>");
                return;
            }

            UUID userId;
            if (!ConsoleUtil.TryParseConsoleUuid(MainConsole.Instance, cmd[2], out userId))
                return;

            UserData ud;

            if(!GetUser(userId, out ud))
            {
                MainConsole.Instance.OutputFormat("No name known for user with id {0}", userId);
                return;
            }

            ConsoleDisplayTable cdt = new ConsoleDisplayTable();
            cdt.AddColumn("UUID", 36);
            cdt.AddColumn("Name", 30);
            cdt.AddColumn("HomeURL", 40);
            cdt.AddRow(userId, string.Format("{0} {1}", ud.FirstName, ud.LastName), ud.HomeURL);

            MainConsole.Instance.Output(cdt.ToString());
        }

        protected virtual void HandleShowUsers(string module, string[] cmd)
        {
            ConsoleDisplayTable cdt = new ConsoleDisplayTable();
            cdt.AddColumn("UUID", 36);
            cdt.AddColumn("Name", 30);
            cdt.AddColumn("HomeURL", 40);
            cdt.AddColumn("Checked", 10);

            Dictionary<UUID, UserData> copyDict;
            lock(m_UserCache)
            {
                copyDict = new Dictionary<UUID, UserData>(m_UserCache);
            }

            foreach(KeyValuePair<UUID, UserData> kvp in copyDict)
            {
                cdt.AddRow(kvp.Key, string.Format("{0} {1}", kvp.Value.FirstName, kvp.Value.LastName), kvp.Value.HomeURL, kvp.Value.HasGridUserTried ? "yes" : "no");
            }

            MainConsole.Instance.Output(cdt.ToString());
        }

        private void UserDataRequest( IClientAPI client, UUID uuid )
        {
            try
            {
                UserData user;
                GetUser(uuid, out user); 
                
                // Even if GetUser returns false we need to tell the client the Unknown name.
                if (client.IsActive)
                    client.SendNameReply(uuid, user.FirstName, user.LastName);
            }
            catch { }
        }

        /// <summary>
        /// Add a user and make sure it is requested from the local or remote grid when needed.
        /// </summary>
        /// <remarks>
        /// If an account is found for the UUID, then the names in this will be used rather than any information
        /// extracted from creatorData.
        /// </remarks>
        /// <param name="uuid"></param>
        /// <param name="creatorData">The creator data for this user.</param>
        public virtual void TriggerAddUser(UUID uuid, string CreatorData)
        {
            m_queue.Enqueue(delegate
            {
                // First we add.
                AddUser(uuid, CreatorData);

                // Then we get, which will trigger a remote grid fetch if needed.
                UserData user;
                GetUser(uuid, out user);
            });
        }
    }
}
