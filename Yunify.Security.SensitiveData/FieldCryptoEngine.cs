﻿using MessagePack;
using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Yunify.Security.Encryption.Provider;

namespace Yunify.Security.SensitiveData
{
    public class FieldCryptoEngine
    {
        private readonly IEncryptionProvider _provider;

        public FieldCryptoEngine(IEncryptionProvider provider)
        {
            _provider = provider;
        }


        public virtual Task EncryptAsync<T>(T o) where T : class
        {
            string keyId = ValidateAndGetSensitiveDataKeyId(o);
            return EncryptAsync(keyId, o);
        }

        public virtual async Task EncryptAsync<T>(string userId, T o) where T : class
        {
            // Loop through object fields and find all fields with [SensitiveDataAttribute]
            var members = o.GetSensitiveDataMembers();
            dynamic encryptMember = null;
            dynamic val = null;
            byte[] serializedVal = null;
            Type underlyingType;
            SensitiveDataAttribute attr;

            MemberInfo srcMember;
            MemberInfo destMember;

            for (int i = 0; i < members.Length; i++)
            {
                srcMember = members[i];
                encryptMember = srcMember;
                underlyingType = srcMember.GetUnderlyingType();

                val = encryptMember.GetValue(o);

                if (val != null)
                {
                    // Check if attribute is serialized to another Member
                    attr = srcMember.GetCustomAttribute<SensitiveDataAttribute>();

                    if (!string.IsNullOrWhiteSpace(attr.SerializeToMember))
                    {
                        // 1. Get destination member
                        destMember = o.FindMemberByName(attr.SerializeToMember);

                        // 2. Validate destination member
                        ValidateDestionationMember(srcMember, destMember);

                        // 3. Set destination member as member where the encrypted value should be stored into.
                        encryptMember = (destMember as dynamic);

                        // 4. Set member value to Default constructor value
                        (srcMember as dynamic).SetValue(o, underlyingType.GetDefault());
                    }


                    // Do actual encryption to dest field
                    if (underlyingType == typeof(string))
                    {
                        encryptMember.SetValue(o, await _provider.EncryptAsync(userId, Encoding.UTF8.GetBytes(val)));
                    }
                    else 
                    {
                        // Serialize to binary formatter with MessagePack
                        serializedVal = MessagePackSerializer.Typeless.Serialize(val);

                        encryptMember.SetValue(o, await _provider.EncryptAsync(userId, serializedVal));
                    }

                }
            }
        }


        public virtual Task DecryptAsync<T>(T o) where T : class
        {
            string keyId = ValidateAndGetSensitiveDataKeyId(o);
            return DecryptAsync(keyId, o);
        }

        public virtual async Task DecryptAsync<T>(string userId, T o) where T : class
        {
            var members = o.GetSensitiveDataMembers();
            dynamic encryptMember = null;
            string val = null;
            byte[] serializedVal = null;
            Type underlyingType;
            SensitiveDataAttribute attr;

            MemberInfo srcMember;
            MemberInfo destMember;

            for (int i = 0; i < members.Length; i++)
            {
                srcMember = members[i];
                encryptMember = srcMember;
                underlyingType = srcMember.GetUnderlyingType();

                // Check if attribute is serialized to another Member
                attr = srcMember.GetCustomAttribute<SensitiveDataAttribute>();

                if (!string.IsNullOrWhiteSpace(attr.SerializeToMember))
                {
                    // 1. Get destination member
                    destMember = o.FindMemberByName(attr.SerializeToMember);

                    // 2. Validate destination member
                    ValidateDestionationMember(srcMember, destMember);

                    // 3. Set destination member as member where the encrypted value is stored into.
                    encryptMember = (destMember as dynamic);
                }

                // a. Get value out of Encrypt Member
                val = encryptMember.GetValue(o);

                if (val != null)
                {
                    // b. Clear the encrypted string
                    encryptMember.SetValue(o, null);

                    // c. Decypt and store value back into src member
                    if (underlyingType == typeof(string))
                    {
                        (srcMember as dynamic).SetValue(o, Encoding.UTF8.GetString(await _provider.DecryptAsync(userId, val)));
                    }
                    else
                    {
                        serializedVal = await _provider.DecryptAsync(userId, val);

                        // Deserialize to binary back to object with MessagePack
                        var obj = MessagePackSerializer.Typeless.Deserialize(serializedVal);

                        (srcMember as dynamic).SetValue(o, obj);
                    }
                }
            }
        }



        private void ValidateDestionationMember(MemberInfo sourceMember, MemberInfo destinationMember)
        {
            // 1. Check if destination member exists
            if (destinationMember == null)
            {
                throw new ArgumentException($"Member '{sourceMember.Name}' reference to another member '{destinationMember.Name}' which doesn't exists. Correct the value of Property: '{nameof(SensitiveDataAttribute.SerializeToMember)}'");
            }

            // 2. check if destination member is of type String
            if (destinationMember.GetUnderlyingType() != typeof(string))
            {
                throw new ArgumentException($"Member '{sourceMember.Name}' reference to another member '{destinationMember.Name}' which isn't of type String. Either switch type of Member or choose another Member to Serialize to");
            }
        }

        private string ValidateAndGetSensitiveDataKeyId<T>(T o) where T : class
        {
            dynamic member;
            dynamic val;
            string keyId;

            // Loop through object fields and find all fields with [SensitiveDataKeyIdAttribute]
            var members = o.GetSensitiveDataKeyIdMembers();

            if (members.Length > 1)
                throw new Exception($"The Class {nameof(o)} has multiple [SensitiveDataKeyId] Attributes defined. Only one is allowed!");
            else if (members.Length == 0)
                throw new Exception($"The Class {nameof(o)} contains no [SensitiveDataKeyId] Attributes. In order to to call this method, there " +
                    $"should be one [SensitiveDataKeyId] specified.");
            else
            {
                member = members[0];
                val = member.GetValue(o);
                keyId = val?.ToString();
               

                if (string.IsNullOrWhiteSpace(keyId))
                    throw new Exception("The member with [SensitiveDataKeyId] applied contains either a NULL value or an Emtpy String.");

                // Validation passed...
                return keyId;
            }
        }
    }
}
