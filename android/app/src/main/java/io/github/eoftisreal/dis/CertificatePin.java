package io.github.eoftisreal.dis;

import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import javax.net.ssl.X509TrustManager;

/** Trust is the explicit leaf-certificate identity in the out-of-band pairing link. */
@SuppressWarnings("CustomX509TrustManager") // Deliberate pinning, not accept-all; positive/negative tests in WireTest.
public final class CertificatePin implements X509TrustManager {
    private final byte[] expected;
    public CertificatePin(byte[] expected) {
        if (expected==null || expected.length!=32) throw new IllegalArgumentException("A SHA-256 certificate pin is required");
        this.expected=expected.clone();
    }
    @Override public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
    @Override public void checkClientTrusted(X509Certificate[] chain,String auth) throws CertificateException {
        throw new CertificateException("Client certificate authentication is unsupported");
    }
    @Override public void checkServerTrusted(X509Certificate[] chain,String auth) throws CertificateException {
        if (chain==null || chain.length==0 || chain[0]==null) throw new CertificateException("Missing server certificate");
        chain[0].checkValidity();
        try {
            if (!MessageDigest.isEqual(expected,MessageDigest.getInstance("SHA-256").digest(chain[0].getEncoded())))
                throw new CertificateException("Server certificate does not match the connection link");
        } catch (GeneralSecurityException e) { throw new CertificateException("Certificate pin verification failed",e); }
    }
}
