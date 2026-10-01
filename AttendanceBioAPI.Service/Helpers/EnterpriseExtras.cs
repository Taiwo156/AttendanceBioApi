using System;

namespace PowerAPI.Service.Helper
{
    public class EnterpriseExtras
    {
        public static string doConvertPwd(string pwd)
        {
            int pwdLen;
            string vPwd;
            string vConvertedPwd;

            vPwd = pwd;
            pwdLen = pwd.Length;
            vConvertedPwd = "";

            if (pwdLen > 0)
            {
                int pcount;
                int textStep;
                string myxter;
                string convertdPwd;
                string myxtercoded;
                pcount = 0;
                pcount = pwdLen;
                convertdPwd = "";
                textStep = 0;
                myxter = "";


                while (pcount > 0)
                {
                    myxter = vPwd.Substring(textStep, 1);
                    myxtercoded = PowerEncode.FncEncodex(ref myxter);
                    convertdPwd = convertdPwd + myxtercoded;
                    textStep = textStep + 1;
                    pcount = pcount - 1;
                    vConvertedPwd = convertdPwd;
                }

                return vConvertedPwd;
            }
            else
            {
                return vConvertedPwd = "";
            }
        }

        public static string CreateRandomPassword(int length = 8)
        {
            // Create a string of characters, numbers, special characters that allowed in the password  
            string validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$&*?_-";
            Random random = new Random();

            // Select one random character at a time from the string  
            // and create an array of chars  
            char[] chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = validChars[random.Next(0, validChars.Length)];
            }
            return new string(chars);
        }
    }
}
