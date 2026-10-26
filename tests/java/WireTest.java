package io.github.eoftisreal.dis;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.util.Arrays;
import java.util.HexFormat;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.cert.CertificateException;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;

public final class WireTest {
    private static int checks;
    public static void main(String[] args) throws Exception {
        byte[] golden=HexFormat.of().parseHex("44495331010000000000000300000007616263");
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();
        Wire.write(new DataOutputStream(bytes),new Wire.Packet(Wire.HELLO,7,new byte[]{97,98,99}));
        require(Arrays.equals(bytes.toByteArray(),golden));
        Wire.Packet read=Wire.read(new DataInputStream(new ByteArrayInputStream(golden)));
        require(read.type==Wire.HELLO && read.sequence==7 && Arrays.equals(read.payload,new byte[]{97,98,99}));
        for (int n=0;n<golden.length;n++) {
            final byte[] cut=Arrays.copyOf(golden,n);
            rejects(() -> Wire.read(new DataInputStream(new ByteArrayInputStream(cut))));
        }
        for (int index:new int[]{0,4,5,6,7}) {
            byte[] bad=golden.clone(); bad[index]=(byte)255;
            rejects(() -> Wire.read(new DataInputStream(new ByteArrayInputStream(bad))));
        }
        rejects(() -> Wire.validateLength(Wire.FRAME,-1));
        rejects(() -> Wire.validateLength(Wire.FRAME,Integer.MAX_VALUE));
        require(HexFormat.of().formatHex(Wire.pointer(1,1,0,.5f,.25f,.75f,-15,30)).equals("010100003f0000003e8000003f400000c170000041f00000"));
        String link="dis://127.0.0.1:49830?fp="+"ab".repeat(32)+"&token="+"cd".repeat(32);
        ConnectionSpec spec=ConnectionSpec.parse(link);
        require(spec.host.equals("127.0.0.1") && spec.port==49830 && spec.fingerprint.length==32);
        rejectsUri(link+"&token="+"ff".repeat(32));
        rejectsUri(link+"#fragment");
        rejectsUri(link.replace("dis:","http:"));
        rejectsUri(link.replace(":49830",":80"));
        rejectsUri(link.replace("&token=","&unknown="));
        Wire.validateLength(Wire.CODEC_CONFIG,16);
        rejects(() -> Wire.validateLength(Wire.CODEC_CONFIG,65537));
        byte[][] parameterSets=H264ParameterSets.parse(HexFormat.of().parseHex("000000016742001e0000000168ce06e2"));
        require(HexFormat.of().formatHex(parameterSets[0]).equals("000000016742001e"));
        require(HexFormat.of().formatHex(parameterSets[1]).equals("0000000168ce06e2"));
        rejects(() -> H264ParameterSets.parse(new byte[]{0,0,1,0x65,1}));
        X509Certificate certificate;
        try (var input=Files.newInputStream(Path.of("tests/fixtures/tls-pin-cert.pem"))) {
            certificate=(X509Certificate)CertificateFactory.getInstance("X.509").generateCertificate(input);
        }
        byte[] pin=MessageDigest.getInstance("SHA-256").digest(certificate.getEncoded());
        new CertificatePin(pin).checkServerTrusted(new X509Certificate[]{certificate},"RSA"); checks++;
        rejectsCertificate(() -> new CertificatePin(new byte[32]).checkServerTrusted(new X509Certificate[]{certificate},"RSA"));
        rejectsCertificate(() -> new CertificatePin(pin).checkServerTrusted(new X509Certificate[0],"RSA"));
        rejectsCertificate(() -> new CertificatePin(pin).checkServerTrusted(null,"RSA"));
        rejectsCertificate(() -> new CertificatePin(pin).checkClientTrusted(new X509Certificate[]{certificate},"RSA"));
        System.out.println(checks+" Java protocol and connection checks passed.");
    }
    private static void require(boolean condition) { if (!condition) throw new AssertionError(); checks++; }
    private static void rejects(Checked action) throws Exception { try { action.run(); } catch (IOException expected) { checks++; return; } throw new AssertionError("Expected IOException"); }
    private static void rejectsUri(String uri) { try { ConnectionSpec.parse(uri); } catch (IllegalArgumentException expected) { checks++; return; } throw new AssertionError("Expected invalid URI"); }
    private static void rejectsCertificate(Checked action) throws Exception { try { action.run(); } catch (CertificateException expected) { checks++; return; } throw new AssertionError("Expected certificate rejection"); }
    private interface Checked { void run() throws Exception; }
}
