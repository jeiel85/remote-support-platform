# Official References

Baseline checked around 2026-07-01. Revalidate versions and security notices before implementation and each release.

## Windows capture/input/service

- Microsoft — Desktop Duplication API  
  https://learn.microsoft.com/windows/win32/direct3ddxgi/desktop-dup-api
- Microsoft — Desktop Duplication for remote desktop scenarios  
  https://learn.microsoft.com/windows-hardware/drivers/display/desktop-duplication-api
- Microsoft — Windows.Graphics.Capture screen capture  
  https://learn.microsoft.com/windows/apps/develop/media-authoring-processing/screen-capture
- Microsoft — SendInput and UIPI restrictions  
  https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput
- Microsoft — Service Session 0 changes  
  https://learn.microsoft.com/windows/win32/services/service-changes-for-windows-vista
- Microsoft — SignTool  
  https://learn.microsoft.com/windows/win32/seccrypto/signtool
- Microsoft — Smart App Control code signing  
  https://learn.microsoft.com/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control

## Runtime

- Microsoft — .NET supported versions  
  https://dotnet.microsoft.com/download/dotnet
- Microsoft — .NET support policy  
  https://dotnet.microsoft.com/platform/support/policy/dotnet-core

At bundle audit, .NET 10 was active LTS and the official download page showed servicing release 10.0.9. Production must remain current on supported patches.

## Database

- PostgreSQL current documentation and supported releases  
  https://www.postgresql.org/docs/current/
- PostgreSQL release notes  
  https://www.postgresql.org/docs/release/

At bundle audit, PostgreSQL 18.4 was the current PostgreSQL 18 minor release shown by the official project. Production must follow supported-minor updates after validation.

## WebRTC, ICE, STUN and TURN

- W3C — WebRTC specification  
  https://www.w3.org/TR/webrtc/
- IETF RFC 8445 — ICE  
  https://www.rfc-editor.org/rfc/rfc8445
- IETF RFC 8489 — STUN  
  https://www.rfc-editor.org/rfc/rfc8489
- IETF RFC 8656 — TURN  
  https://www.rfc-editor.org/rfc/rfc8656
- IETF RFC 8831 — WebRTC Data Channels  
  https://www.rfc-editor.org/rfc/rfc8831
- coturn project  
  https://github.com/coturn/coturn
- coturn releases  
  https://github.com/coturn/coturn/releases

At bundle audit, coturn 4.14.0 and immutable Docker image revision 4.14.0-r0 were visible upstream. Production must use the latest security-patched validated version rather than relying permanently on this number.

## Application and development security

- OWASP ASVS project  
  https://owasp.org/www-project-application-security-verification-standard/
- NIST SP 800-218 Secure Software Development Framework  
  https://csrc.nist.gov/pubs/sp/800/218/final
- The Update Framework  
  https://theupdateframework.io/

## Korea privacy/compliance starting points

- 국가법령정보센터 — 개인정보 보호법/시행령 검색  
  https://www.law.go.kr/
- 개인정보보호위원회 — 개인정보의 안전성 확보조치 기준 안내서  
  https://www.pipc.go.kr/

The compliance documents intentionally avoid definitive legal conclusions. Obtain current legal advice before launch.

## Optional cache/backplane

- Valkey project and releases  
  https://valkey.io/download/

At bundle audit, Valkey 9.1.0 was the current GA release shown upstream. It is optional; PostgreSQL remains the source of truth.
