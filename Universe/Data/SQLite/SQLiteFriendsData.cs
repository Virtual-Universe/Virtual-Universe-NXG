/*
 * Copyright (c) Contributors, http://virtual-planets.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the Virtual Universe Project nor the
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
using System.Collections;
using System.Collections.Generic;
using System.Data;
using OpenMetaverse;
using Universe.Framework;
#if CSharpSqlite
    using Community.CsharpSqlite.Sqlite;
#else
    using Mono.Data.Sqlite;
#endif

namespace Universe.Data.SQLite
{
    public class SQLiteFriendsData : SQLiteGenericTableHandler<FriendsData>, IFriendsData
    {
        public SQLiteFriendsData(string connectionString, string realm)
            : base(connectionString, realm, "FriendsStore")
        {
        }

        public FriendsData[] GetFriends(UUID principalID)
        {
            return GetFriends(principalID.ToString());
        }

        public FriendsData[] GetFriends(string userID)
        {
            using (SqliteCommand cmd = new SqliteCommand())
            {
                cmd.CommandText = String.Format("select a.*,case when b.Flags is null then -1 else b.Flags end as TheirFlags from {0} as a left join {0} as b on a.PrincipalID = b.Friend and a.Friend = b.PrincipalID where a.PrincipalID = :PrincipalID", m_Realm);
                cmd.Parameters.AddWithValue(":PrincipalID", userID.ToString());

                return DoQuery(cmd);
            }
        }

        public bool Delete(UUID principalID, string friend)
        {
            return Delete(principalID.ToString(), friend);
        }

        public override bool Delete(string principalID, string friend)
        {
            using (SqliteCommand cmd = new SqliteCommand())
            {
                cmd.CommandText = String.Format("delete from {0} where PrincipalID = :PrincipalID and Friend = :Friend", m_Realm);
                cmd.Parameters.AddWithValue(":PrincipalID", principalID.ToString());
                cmd.Parameters.AddWithValue(":Friend", friend);

                ExecuteNonQuery(cmd, m_Connection);
            }

            return true;
        }

    }
}
