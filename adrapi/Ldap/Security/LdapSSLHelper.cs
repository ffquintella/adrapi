using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace adrapi.Ldap.Security
{
    public static class LdapSSLHelper
    {
        public static bool HandleRemoteCertificateValidationCallback(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            int a = 0;
            a++;
            return true;
        }

        public static bool LocalSSLHandler(X509Certificate certificate, int[] certificateErrors)
        {

            X509Store store = null;
            //X509Stores stores = X509StoreManager.LocalMachine;
            //store = stores.TrustedRoot;

            store = new X509Store(StoreLocation.LocalMachine);

            //Import the details of the certificate from the server.

            X509Certificate x509 = null;
            X509CertificateCollection coll = new X509CertificateCollection();
            byte[] data = certificate.GetRawCertData();
            if (data != null)
                x509 = new X509Certificate(data);

            //List the details of the Server

            //if (bindCount == 1)
            //{

            /*Response.Write("<b><u>CERTIFICATE DETAILS:</b></u> <br>");
            Response.Write("  Self Signed = " + x509.IsSelfSigned + "  X.509  version=" + x509.Version + "<br>");
            Response.Write("  Serial Number: " + CryptoConvert.ToHex(x509.SerialNumber) + "<br>");
            Response.Write("  Issuer Name:   " + x509.IssuerName.ToString() + "<br>");
            Response.Write("  Subject Name:  " + x509.SubjectName.ToString() + "<br>");
            Response.Write("  Valid From:    " + x509.ValidFrom.ToString() + "<br>");
            Response.Write("  Valid Until:   " + x509.ValidUntil.ToString() + "<br>");
            Response.Write("  Unique Hash:   " + CryptoConvert.ToHex(x509.Hash).ToString() + "<br>");
            */
                       
            // }

            var bHowToProceed = true;
            if (bHowToProceed == true)
            {
                //Add the certificate to the store. This is \Documents and Settings\program data\.mono. . .
                if (x509 != null)
                    coll.Add(x509);

                store.Add((X509Certificate2)x509);
                
                //store.Import(x509);

                //if (bindCount == 1)
                //    removeFlag = true;
            }


            return bHowToProceed;
        }
    }
}
