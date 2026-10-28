# IdentityServer de prueba (Duende)

Host mínimo para probar el modo `Jwt` con Duende IdentityServer (P-078): un cliente SPA público con PKCE
(`mantenedores-spa`), el ApiResource `mantenedores-api` con el scope `mantenedores` y dos usuarios de prueba
(`ana` / `ana` con rol `Administradores`, `luis` / `luis` con rol `Lectores`).

```bash
cd samples/duende
TOKEN_SECONDS=70 dotnet run --urls http://localhost:5300     # tokens cortos para ver la renovación silenciosa
```

La app, en `http://localhost:8094`, con la configuración de Duende de `docs/configuracion.md` y además
`Auth__Jwt__Authority=http://localhost:5300` y `Auth__Jwt__RequireHttpsMetadata=false`.

Solo para pruebas: usuarios y clientes en memoria, llave de firma de desarrollo y cookies `Lax` porque va por `http`.
Duende IdentityServer necesita licencia para usarse en producción (ver duendesoftware.com).
