# PosterPrintRequest --- Guía de instalación IIS y especificación del instalador

**Estado:** borrador operativo y especificación pendiente de cierre\
**Fecha de elaboración:** 7 de noviembre de 2026\
**Aplicación:** PosterPrintRequest\
**Plataforma:** ASP.NET Core / .NET 10, Blazor Server, SQL Server, IIS y
almacenamiento compartido Qumulo SMB\
**Ámbito:** instalación desde cero en un servidor Windows/IIS,
configuración de la aplicación, aprovisionamiento del archivo de
credenciales DPAPI y requisitos de un instalador interactivo para
administradores.

> **Importante --- límites de autoridad**
>
> Este documento organiza los pasos y transcribe requisitos de los
> contratos vigentes. No sustituye ni modifica el roadmap ni los
> contratos. Los datos que los contratos no determinan se identifican
> expresamente como **pendientes de confirmación**. No deben convertirse
> en valores predeterminados del instalador por inferencia.
>
> La implementación de PHASE 12 está autorizada por el roadmap. La
> aceptación de producción sigue condicionada a las confirmaciones
> externas E1--E5 y a las pruebas de producción definidas en
> `Storage_Access_Contract_v1.docx`, §§13--14.

------------------------------------------------------------------------

## 1. Autoridad documental y precedencia

### 1.1 Precedencia entre documentos del proyecto

Aplicar esta jerarquía sin mezclar responsabilidades:

1.  **`docs/Poster_Print_Request_System_Roadmap_v4_Inventory.docx`**\
    Única autoridad para orden, estado, promoción y siguiente fase. El
    roadmap autoriza PHASE 12 --- Qumulo SMB Storage Access. Este
    documento no cambia su estado ni su orden.

2.  **`docs/Storage_Access_Contract_v1.docx`**\
    Autoridad exclusiva para la frontera de acceso al almacenamiento,
    identidad SMB, modos de acceso, protección y rotación de
    credenciales, comportamiento por contexto, permisos y fallos.

3.  **`docs/Shared_Storage_Contract_v1.docx`**\
    Autoridad exclusiva para estructura de carpetas, rutas, colocación,
    migración, movimiento al marcar Printed y reversión ante fallos.
    Storage Access no puede redefinir estas reglas.

4.  **Contrato de inventario de consumibles**\
    Autoridad de su propio alcance. No modifica la identidad SMB ni la
    estructura de almacenamiento compartido.

5.  **Este documento**\
    Guía operativa y requisitos propuestos del instalador. No puede
    ampliar, modificar ni contradecir los documentos anteriores.

### 1.2 Precedencia de configuración de .NET

Para una aplicación ASP.NET Core que usa la configuración predeterminada
de `WebApplication.CreateBuilder`, la prioridad habitual de los
proveedores de configuración de aplicación, de mayor a menor, es:

1.  Argumentos de línea de comandos.
2.  Variables de entorno.
3.  User Secrets, solo en Development.
4.  `appsettings.{Environment}.json` --- por ejemplo,
    `appsettings.Production.json`.
5.  `appsettings.json`.

Un proveedor agregado después puede sobrescribir el valor anterior. La
configuración real debe comprobarse en `Program.cs`; el instalador no
debe asumir un orden distinto sin evidencia del código.

**Consecuencia práctica:** los valores suministrados como variables de
entorno a nivel de aplicación IIS prevalecen normalmente sobre los
valores equivalentes de los archivos JSON. Para claves jerárquicas .NET
se usa doble guion bajo (`__`) en variables de entorno.

Ejemplos autorizados por el contrato de almacenamiento:

-   `SharedStorage__RootPath`
-   `SharedStorage__CredentialFile`
-   `ConnectionStrings__PosterPrintRequest`

La contraseña Qumulo **no** se configura como variable de entorno ni
como texto en `appsettings.json`. Solo se guarda dentro del archivo
cifrado DPAPI.

------------------------------------------------------------------------

## 2. Alcance funcional y decisiones que no se deben alterar

-   La aplicación se ejecuta en Windows Server bajo IIS.
-   El inicio de sesión existente mediante Windows Negotiate se
    conserva.
-   La conexión a SQL Server continúa usando Windows Integrated
    Authentication.
-   El almacenamiento compartido designado es Qumulo mediante SMB.
-   La cuenta usada contra Qumulo es una cuenta de servicio local de
    Qumulo dedicada. No es la identidad del Application Pool, la cuenta
    de SQL Server ni una cuenta personal.
-   Qumulo REST API queda excluida.
-   No se permite fallback a la identidad del proceso ni a otra
    credencial cuando una operación SMB falla.
-   No se cambia la estructura de almacenamiento ni las reglas de
    migración/Printed del Shared Storage Contract.
-   El proceso de IIS debe ejecutarse con un único worker process.
-   La credencial se lee al arrancar. No hay recarga en caliente.

------------------------------------------------------------------------

## 3. Paso 0 --- inventario y preparación antes de instalar

Antes de tocar el servidor, el administrador y los responsables de
infraestructura deben disponer de lo siguiente:

### 3.1 Información de la aplicación

-   Paquete de publicación aprobado de PosterPrintRequest.
-   Versión de .NET objetivo confirmada por el artefacto publicado
    (`net10.0` en el estado actual del proyecto).
-   Nombre del sitio IIS y nombre del Application Pool: **los determina
    el administrador y deben registrarse**.
-   Ruta física de instalación de la aplicación: **la determina el
    administrador**.
-   Nombre DNS/host, puerto, binding y certificado HTTPS: **pendientes
    de los estándares institucionales**.
-   Entorno de ejecución: `Production`.
-   Configuración de SQL Server: servidor/instancia y base de datos
    correctos. La autenticación permanece integrada de Windows.

### 3.2 Información de Qumulo e infraestructura

IT debe proporcionar y confirmar:

-   UNC completo del share Qumulo que se usará como
    `SharedStorage:RootPath`.
-   Nombre exacto de dominio/autoridad que debe incluirse en el campo
    `Domain` de la credencial.
-   Nombre de la cuenta local de servicio Qumulo.
-   Contraseña inicial, entregada por un canal seguro al administrador
    que aprovisiona la credencial.
-   Confirmación de autenticación SMB con NTLMv2 y forma exacta del
    nombre de cuenta.
-   Confirmación de política NTLM saliente en el host IIS.
-   Confirmación de TCP 445 entre IIS y Qumulo.
-   Dialecto SMB y requisitos institucionales de signing/encryption.
-   Permisos de share y filesystem requeridos por §8.
-   Ubicación del archivo de credenciales fuera de la aplicación, del
    directorio servido por IIS, del repositorio y del paquete de
    despliegue.

### 3.3 Requisitos de acceso del administrador

El administrador que realiza la instalación necesita privilegios locales
suficientes para:

-   Instalar roles/servicios de Windows e IIS, si aún no están
    instalados.
-   Instalar el .NET Hosting Bundle.
-   Crear/configurar el sitio y el Application Pool.
-   Crear directorios de instalación y de credenciales.
-   Configurar variables de entorno de la aplicación IIS.
-   Aplicar y verificar ACL NTFS.
-   Ejecutar el aprovisionamiento DPAPI en el propio servidor IIS.
-   Reciclar el Application Pool y realizar las verificaciones.

El administrador de IIS no obtiene por ello permisos administrativos
sobre Qumulo. La cuenta de servicio Qumulo se administra por el equipo
autorizado de Qumulo/IT.

------------------------------------------------------------------------

## 4. Paso 1 --- instalar y habilitar los componentes de Windows

### 4.1 Sistema operativo y rol IIS

En el servidor Windows aprobado:

1.  Confirmar que el sistema operativo/versión está soportado por la
    política institucional y por la versión de .NET seleccionada.
2.  Abrir Server Manager.
3.  Usar **Add Roles and Features**.
4.  Instalar el rol **Web Server (IIS)**.
5.  Incluir la consola de administración IIS.
6.  Incluir los servicios de rol necesarios para alojar ASP.NET Core.
7.  Incluir **Windows Authentication** porque la aplicación conserva
    Windows Negotiate.
8.  Confirmar con el administrador de seguridad si se requieren otros
    módulos/servicios IIS. No instalar módulos opcionales sin necesidad
    documentada.

### 4.2 .NET 10 Hosting Bundle

Después de instalar IIS:

1.  Descargar el **.NET 10 Hosting Bundle** desde Microsoft.
2.  Ejecutar el instalador en el servidor IIS.
3.  Confirmar que instala el runtime requerido y ASP.NET Core Module
    (ANCM).
4.  Si el Hosting Bundle se instaló antes que IIS, volver a
    ejecutar/reparar el Hosting Bundle después de instalar IIS.
5.  Reiniciar IIS o el servidor según el procedimiento Microsoft y la
    ventana de cambio aprobada.

El Hosting Bundle incluye los componentes para alojar aplicaciones
ASP.NET Core detrás de IIS. No instalar el SDK de .NET en producción
salvo que exista una necesidad operativa separada y autorizada. La
compilación/publicación se realiza normalmente en el entorno de build.

### 4.3 Componentes no autorizados por defecto

-   No instalar herramientas de Qumulo REST API: REST API está excluida.
-   No instalar herramientas de impresión directa.
-   No instalar runtimes o módulos adicionales sin una dependencia
    comprobada del paquete de publicación.
-   No instalar SQL Server en el servidor IIS por el mero hecho de
    alojar la aplicación; SQL Server es un servicio separado y su
    ubicación la determina la arquitectura existente.

------------------------------------------------------------------------

## 5. Paso 2 --- preparar SQL Server y la autenticación existente

1.  Confirmar que la base de datos de PosterPrintRequest existe y que el
    servidor IIS puede alcanzar el SQL Server configurado.
2.  Mantener **Windows Integrated Authentication** en la cadena de
    conexión.
3.  La identidad efectiva usada para la conexión SQL debe ser la que
    resulte del modelo de hosting y de la configuración SQL ya aprobada.
    No sustituirla por la cuenta Qumulo.
4.  Confirmar con el DBA los permisos de base de datos necesarios para
    la identidad que realmente abre la conexión.
5.  Configurar el valor de conexión mediante el mecanismo de
    configuración aprobado para IIS, por ejemplo
    `ConnectionStrings__PosterPrintRequest`, si la aplicación lo consume
    desde el proveedor estándar de variables de entorno.
6.  No almacenar contraseñas SQL en archivos JSON ni en el instalador.

**Pendiente de verificación en el código:** confirmar la clave exacta de
conexión y la identidad SQL efectiva en el `Program.cs`/registro de
persistencia final. El instalador debe reflejar el código real, no
inventar el nombre de la clave o el principal SQL.

------------------------------------------------------------------------

## 6. Paso 3 --- crear el sitio IIS y el Application Pool

Los siguientes valores deben ser introducidos o confirmados por el
administrador durante la instalación:

-   Nombre del sitio IIS.
-   Nombre del Application Pool.
-   Ruta física de la aplicación publicada.
-   Binding (protocolo, IP si aplica, puerto y host name).
-   Certificado HTTPS y binding TLS, según la política institucional.
-   Identidad del Application Pool.
-   Entorno `Production`.

Reglas contractuales:

-   Registrar el nombre real del Application Pool; se necesita para
    construir la ACL del archivo de credenciales.
-   Configurar **un solo worker process**
    (`Maximum Worker Processes = 1`).
-   No activar identidades alternativas ni fallback para SMB.
-   Mantener el mecanismo de Windows Negotiate existente.
-   No cambiar la identidad SMB dedicada por la identidad del
    Application Pool.

**Pendiente de confirmación:** el contrato de almacenamiento no fija el
nombre del sitio, nombre del pool, ruta física, binding, puerto,
certificado, idle timeout, recycling schedule ni política de preload.
Esos valores pertenecen a la administración IIS/institucional y no deben
codificarse como defaults sin autorización.

------------------------------------------------------------------------

## 7. Paso 4 --- desplegar la publicación de la aplicación

El flujo de publicación utilizado para el proyecto es:

1.  Compilar/publicar en Release en el entorno de build.
2.  Usar `dotnet publish` para generar el directorio de publicación.
3.  Copiar el contenido del directorio `publish` al directorio físico
    configurado para la aplicación IIS.
4.  Verificar que el `web.config` y los archivos publicados pertenecen
    al mismo build.
5.  No copiar archivos de desarrollo ni secretos al directorio
    publicado.
6.  No incluir el archivo de credenciales Qumulo en el directorio de la
    aplicación ni en el paquete ZIP.
7.  No editar manualmente el contenido de publicación para introducir el
    UNC, la cuenta o la contraseña.

Ejemplo de comando de publicación (en el entorno de build, no
necesariamente en producción):

``` powershell
dotnet publish src\PosterPrintRequest.Web\PosterPrintRequest.Web.csproj -c Release
```

La ruta final del directorio `publish` debe tomarse de la salida real
del build. No se fija aquí una ruta local de máquina.

------------------------------------------------------------------------

## 8. Paso 5 --- configurar el share Qumulo y los permisos

### 8.1 Cuenta de servicio

IT/Qumulo crea o proporciona una cuenta local de servicio dedicada. El
instalador de la aplicación no debe crear esa cuenta en Qumulo ni
cambiar su contraseña salvo que exista una interfaz administrativa
oficialmente autorizada.

La cuenta:

-   Se usa únicamente para el acceso SMB de PosterPrintRequest.
-   No es la cuenta personal del administrador.
-   No es la identidad del Application Pool.
-   No es la cuenta de SQL Server.
-   Debe autenticarse mediante SMB con NTLMv2.
-   NTLMv1 no está permitido.

### 8.2 Permisos requeridos en Qumulo

El contrato exige ambos niveles:

  -----------------------------------------------------------------------
  Nivel                   Permiso requerido       Alcance
  ----------------------- ----------------------- -----------------------
  Share                   Read and Change         Raíz del share y
                                                  contenido
                                                  correspondiente

  Filesystem/ACL          Modify, incluyendo      Raíz compartida y todo
                          Delete y Delete         lo que cuelga de ella
                          subfolders and files    
  -----------------------------------------------------------------------

No se requieren:

-   Change permissions.
-   Take ownership.
-   Administración del cluster.
-   Permisos administrativos globales de Qumulo.

**Advertencia contractual importante:** Read + Write sin Delete **no es
suficiente** para la operación completa. El contrato requiere Delete
porque la aplicación reemplaza directorios/archivos, mueve directorios
completos durante migración y transición Printed, y puede necesitar
revertir un movimiento fallido.

Una prueba limitada de lectura y escritura puede servir como
comprobación inicial de conectividad, pero no constituye aceptación
funcional/producción ni demuestra que la aplicación puede operar
correctamente. La validación contractual completa necesita las
operaciones de movimiento y eliminación definidas en los contratos.

### 8.3 Confirmaciones de red y protocolo

Antes de la prueba de producción, IT confirma:

-   TCP 445 desde el host IIS al cluster Qumulo.
-   SMB 3.x.
-   Requisitos de signing/encryption.
-   Política NTLM saliente del host que permita la autenticación
    requerida.
-   Compatibilidad de la cuenta local con NTLMv2.

------------------------------------------------------------------------

## 9. Paso 6 --- crear el directorio protegido para el secreto cifrado

El archivo de credenciales se almacena en el propio host IIS:

-   Fuera del directorio físico de la aplicación.
-   Fuera de cualquier directorio servido por IIS.
-   Fuera del repositorio.
-   Fuera del paquete de despliegue.
-   En una ruta absoluta elegida por el administrador y confirmada en
    E5.

No se fija una ruta literal en esta guía. El administrador debe
seleccionar una ubicación conforme a la política institucional y
registrar la ruta en el registro de cambio, no en el repositorio.

### 9.1 ACL obligatoria

Deshabilitar la herencia en la carpeta y en el archivo, y establecer
exactamente los principales contractuales:

  Principal                                          Permiso
  -------------------------------------------------- --------------
  `SYSTEM`                                           Full control
  `BUILTIN\Administrators`                           Full control
  `IIS AppPool\<nombre real del Application Pool>`   Read
  Cualquier otro principal                           Sin acceso

La identidad exacta del Application Pool debe confirmarse antes de
construir la ACL.

**Por qué es crítico:** con DPAPI `LocalMachine`, cualquier proceso del
mismo host que pueda leer el archivo puede solicitar su descifrado. La
ACL limita quién puede leer el archivo y, por tanto, quién puede usar la
credencial.

No conceder Modify/Write al Application Pool sobre el archivo de
credenciales. El contrato requiere Read para esa identidad.

------------------------------------------------------------------------

## 10. Paso 7 --- aprovisionar el archivo DPAPI

### 10.1 Formato lógico antes del cifrado

El contenido lógico UTF-8 JSON tiene estas propiedades:

``` json
{
  "Domain": "<dominio o autoridad confirmado por IT>",
  "UserName": "<cuenta local de servicio Qumulo>",
  "Password": "<contraseña recibida por canal seguro>"
}
```

Este JSON es el contenido que el procedimiento de aprovisionamiento
protege. **No debe guardarse en claro en disco.**

### 10.2 Protección obligatoria

El procedimiento de aprovisionamiento debe:

1.  Ejecutarse en el host IIS de destino.

2.  Construir el JSON UTF-8 con `Domain`, `UserName` y `Password`.

3.  Proteger los bytes mediante Windows DPAPI `ProtectedData.Protect`.

4.  Usar `DataProtectionScope.LocalMachine`.

5.  Usar como entropía adicional los bytes UTF-8 exactos de:

    `PosterPrintRequest.SharedStorage.v1`

6.  Escribir únicamente el resultado cifrado en el archivo configurado.

7.  Aplicar la ACL indicada en §9.

8.  Limpiar de memoria el material plaintext tan pronto como sea
    posible.

9.  No imprimir el JSON, contraseña, cuenta ni ruta sensible en consola,
    logs o transcript.

El cifrado debe realizarse en el servidor IIS que descifrará el archivo.
Una copia del archivo DPAPI en otra máquina no debe ser utilizable por
sí sola.

### 10.3 Configuración de la aplicación

En la configuración de entorno del **nivel de la aplicación IIS**,
definir:

  -----------------------------------------------------------------------------
  Variable                                  Valor
  ----------------------------------------- -----------------------------------
  `SharedStorage__RootPath`                 UNC completo del share Qumulo

  `SharedStorage__CredentialFile`           Ruta absoluta del archivo DPAPI
                                            protegido

  `ConnectionStrings__PosterPrintRequest`   Cadena SQL con Windows Integrated
                                            Authentication, si así lo confirma
                                            la configuración real
  -----------------------------------------------------------------------------

No colocar la contraseña en ninguna variable de entorno. La ruta del
archivo cifrado no es el secreto, pero debe tratarse como información
operativa sensible y no registrarse en logs públicos.

El modo Qumulo-account se selecciona al arranque cuando
`SharedStorage:CredentialFile` está configurado. Si no está configurado,
la aplicación usa process-identity mode. En Production, si
`SharedStorage:RootPath` es UNC y no se configuró el archivo de
credenciales, la aplicación debe negarse a arrancar.

------------------------------------------------------------------------

## 11. Paso 8 --- inicialización y comportamiento de arranque

Al iniciar:

1.  La aplicación lee la configuración.
2.  Selecciona el modo una sola vez.
3.  En Qumulo-account mode, comprueba Windows y lee el archivo de
    credenciales.
4.  Descifra con DPAPI LocalMachine y la entropía contractual.
5.  Valida que existan Domain, UserName y Password.
6.  Crea una vez el token mediante `LogonUser` con:
    -   `LOGON32_LOGON_NEW_CREDENTIALS`
    -   `LOGON32_PROVIDER_WINNT50`
7.  Mantiene el token en el servicio singleton durante la vida del
    proceso.
8.  Cada operación de almacenamiento usa
    `WindowsIdentity.RunImpersonated` o `RunImpersonatedAsync`.
9.  Libera el token al apagar el proceso.
10. Limpia la contraseña plaintext después de crear el token.

`LOGON32_LOGON_NEW_CREDENTIALS` no valida la contraseña contra el
servidor en el momento de crear el token. Por eso el servicio intenta
comprobar la raíz bajo ese token.

-   Si el archivo no existe, no se puede leer, no se puede descifrar o
    carece de los campos requeridos, el arranque falla.
-   Si el share no está disponible al arrancar, la aplicación no se
    detiene por ese solo motivo; health y las operaciones que necesiten
    almacenamiento reflejan el fallo.
-   Una operación fallida nunca se reintenta con la identidad del
    proceso ni con otra credencial.

------------------------------------------------------------------------

## 12. Paso 9 --- verificación posterior a la configuración IIS

Realizar en una ventana de cambio autorizada:

1.  Confirmar que las variables de entorno están asociadas a la
    aplicación IIS correcta.
2.  Confirmar que la identidad del Application Pool puede leer el
    archivo cifrado y no puede modificarlo.
3.  Confirmar que SYSTEM y Administrators conservan Full control.
4.  Confirmar que no hay otros principals con acceso al
    directorio/archivo.
5.  Reciclar el Application Pool para que lea la configuración y
    credencial en el arranque.
6.  Comprobar `/health/ready`.
7.  Confirmar que el log no contiene contraseña, contenido del archivo,
    cuenta/dominio, ruta de credencial, raíz UNC, nombre de
    share/servidor ni ruta física.
8.  Ejecutar las pruebas SMB autorizadas con un poster de prueba y datos
    aprobados.
9.  Registrar fecha, operador y evidencia de cada prueba en el registro
    institucional, nunca en el repositorio si contiene información
    sensible.

### 12.1 Prueba limitada de conectividad

Una prueba inicial de lectura y escritura puede comprobar que el
servidor alcanza el share y que la autenticación básica funciona. Esta
prueba es **solo un smoke test**.

### 12.2 Pruebas contractuales completas

La aceptación de producción requiere comprobar, como mínimo:

-   Upload de draft.
-   Poster de evento con Approval Sheet.
-   Descarga inline.
-   Descarga como attachment.
-   Biblioteca del técnico.
-   Transición Printed sobre poster de prueba.
-   Startup migration sin volver a mover un poster ya asentado.
-   Simulacro de rotación.
-   Ausencia de información sensible en logs.
-   `/health/ready` Healthy.

La prueba completa requiere los permisos de §8, incluidos Delete y
Delete subfolders and files. No declarar aceptación de producción
basándose solo en lectura/escritura.

------------------------------------------------------------------------

## 13. Rotación de la credencial

La rotación contractual ocurre cada 12 meses y de inmediato ante
sospecha de exposición.

Procedimiento en una ventana de baja actividad:

1.  IT cambia la contraseña de la cuenta Qumulo.
2.  El administrador genera un nuevo archivo DPAPI junto al actual,
    usando el mismo scope, entropía y ACL.
3.  Reemplaza el archivo actual mediante un único rename.
4.  Recicla el Application Pool.
5.  Verifica `/health/ready`.
6.  Descarga un poster de prueba.
7.  Registra la rotación en el registro institucional de cambios, no en
    el repositorio.

La aplicación lee la credencial solo al arrancar. No recarga el archivo
en caliente.

------------------------------------------------------------------------

## 14. Matriz de responsabilidades y permisos

  --------------------------------------------------------------------------
  Actor                      Permisos/funciones      No debe recibir
  -------------------------- ----------------------- -----------------------
  Administrador local del    Instalar IIS/Hosting    Administración global
  servidor IIS               Bundle, configurar      de Qumulo por defecto
                             sitio/pool, crear       
                             directorios, establecer 
                             ACL, ejecutar DPAPI,    
                             configurar variables y  
                             reciclar pool           

  `SYSTEM` en IIS host       Full control sobre      Uso interactivo de la
                             carpeta/archivo DPAPI   cuenta Qumulo
                             según ACL               

  `BUILTIN\Administrators`   Full control sobre      Permisos Qumulo no
  en IIS host                carpeta/archivo DPAPI   requeridos
                             según ACL               

  `IIS AppPool\<pool>`       Read del archivo DPAPI; Write/Modify del
                             identidad local del     secreto
                             proceso                 

  Cuenta de servicio Qumulo  Share Read + Change;    Change permissions,
                             filesystem Modify       Take ownership,
                             incluyendo Delete y     administración del
                             Delete subfolders/files cluster

  DBA/administrador SQL      Autorizar la identidad  Reutilizar la cuenta
                             Windows que realmente   Qumulo
                             conecta a SQL           

  IT/red/Qumulo              Confirmar E1--E5,       Contraseña almacenada
                             NTLMv2, TCP 445, SMB    en repositorio o logs
                             3.x, ACL y política de  
                             red                     
  --------------------------------------------------------------------------

------------------------------------------------------------------------

## 15. Requisitos del instalador interactivo para administradores

Esta sección define el comportamiento que se desea para el futuro
instalador. **Especificación propuesta; no equivale por sí sola a
autorización contractual para implementar un instalador.** Antes de
desarrollar el instalador, sus responsabilidades y límites deben quedar
autorizados por el documento rector aplicable.

### 15.1 Principios obligatorios

El instalador debe:

-   Ejecutarse con elevación administrativa cuando la tarea lo requiera.
-   Ser interactivo, guiado y validado por etapas.
-   Detectar prerequisitos existentes y mostrar qué falta.
-   No asumir nombres de sitio, pool, rutas, hostnames, puertos ni
    certificados.
-   Solicitar los valores al administrador y mostrar un resumen antes de
    aplicar cambios.
-   Separar configuración local IIS de configuración externa
    Qumulo/SQL/red.
-   No crear ni modificar cuentas Qumulo.
-   No conceder permisos Qumulo.
-   No solicitar credenciales personales.
-   Solicitar la contraseña Qumulo en un control seguro, sin eco,
    argumentos de proceso, transcript ni logs.
-   Crear el archivo DPAPI solo en el host IIS de destino.
-   Aplicar y verificar la ACL exacta.
-   No escribir la contraseña en appsettings, variables de entorno,
    archivos temporales plaintext persistentes ni paquetes.
-   No incluir el secreto cifrado en el directorio del sitio, paquete de
    instalación, repositorio ni logs.
-   No cambiar Windows Negotiate ni SQL Integrated Authentication.
-   No desplegar cambios de base de datos ni alterar contratos.
-   Detenerse ante cualquier precondición fallida, valor contradictorio
    o permiso insuficiente.
-   Ser idempotente: detectar configuración existente y no
    sobrescribirla silenciosamente.
-   Mostrar claramente qué cambios va a realizar y qué cambios requieren
    al equipo IT.

### 15.2 Flujo interactivo propuesto

**Pantalla 0 --- Bienvenida y alcance** - Identificar aplicación y
versión del paquete. - Mostrar entorno destino. - Explicar acciones y
límites. - Mostrar prerequisitos detectados.

**Pantalla 1 --- Prerequisitos del servidor** - Detectar Windows
Server. - Detectar rol IIS y consola de administración. - Detectar
Windows Authentication. - Detectar ASP.NET Core Module y .NET 10 Hosting
Bundle. - Informar qué componente falta. - No instalar automáticamente
sin confirmación y elevación.

**Pantalla 2 --- Sitio y Application Pool** - Solicitar nombre de
sitio. - Solicitar nombre de Application Pool. - Solicitar ruta física
de instalación. - Solicitar binding HTTP/HTTPS, host y certificado según
política institucional. - Seleccionar/confirmar identidad del pool. -
Forzar/validar un worker process. - Confirmar antes de crear o
modificar.

**Pantalla 3 --- Paquete de aplicación** - Seleccionar el directorio del
paquete publicado. - Validar que contiene los artefactos esperados. -
Copiar/publicar a la ruta física elegida. - No copiar secretos ni
configuración de desarrollo. - Validar `web.config` y target framework
del artefacto.

**Pantalla 4 --- SQL Server** - Solicitar servidor/instancia y base de
datos solo si el código final confirma que son valores configurables. -
Mostrar que la autenticación es Windows Integrated Authentication. - No
pedir contraseña SQL. - Confirmar la identidad Windows efectiva y que
DBA otorgó los permisos necesarios. - Configurar la clave real de
conexión confirmada en el código.

**Pantalla 5 --- Qumulo SMB** - Solicitar UNC raíz del share. -
Solicitar ruta absoluta para el archivo DPAPI. - Solicitar Domain y
UserName exactos proporcionados por IT. - Solicitar Password mediante
entrada segura. - Mostrar un checklist E1--E4 de confirmaciones
externas. - No permitir declarar producción lista si las confirmaciones
requeridas faltan.

**Pantalla 6 --- Directorio y protección del secreto** - Validar que la
ruta del archivo está fuera del sitio IIS, del directorio servido, del
repositorio y del paquete. - Crear directorio si el administrador lo
confirma. - Deshabilitar herencia. - Aplicar ACL contractual. -
Verificar ACL efectiva. - Crear archivo DPAPI LocalMachine con la
entropía contractual. - Borrar buffers de plaintext lo antes posible. -
Verificar que el archivo cifrado existe y que el pool puede leerlo.

**Pantalla 7 --- Variables de entorno IIS** - Configurar
`ASPNETCORE_ENVIRONMENT=Production`. - Configurar
`SharedStorage__RootPath`. - Configurar
`SharedStorage__CredentialFile`. - Configurar
`ConnectionStrings__PosterPrintRequest` solo según el contrato/código
real. - No configurar contraseña Qumulo como variable de entorno. -
Mostrar el valor de cada variable no secreta y ocultar cualquier dato
sensible.

**Pantalla 8 --- Revisión y aplicación** - Mostrar resumen de sitio,
pool, rutas, bindings, variables y ACL. - Mostrar qué valores provienen
del administrador y qué confirmaciones provienen de IT. - Requerir
confirmación explícita. - Aplicar cambios en orden. - Si falla un paso,
informar qué operación falló sin revelar valores sensibles y detener el
flujo. - No dejar un estado parcialmente configurado presentado como
exitoso.

**Pantalla 9 --- Reciclado y validación** - Reciclar el Application Pool
seleccionado. - Comprobar arranque. - Comprobar `/health/ready`. -
Ejecutar pruebas autorizadas. - Generar un informe sin secretos, rutas
sensibles ni nombres de cuenta. - Indicar claramente `Installed`,
`Configured`, `Connectivity verified` y
`Production acceptance pending/complete` como estados distintos, sin
confundirlos.

### 15.3 Datos que el instalador debe recopilar

  ----------------------------------------------------------------------------------------------
  Dato                              Origen                   Sensibilidad      Estado
  --------------------------------- ------------------------ ----------------- -----------------
  Nombre del sitio IIS              Administrador            Interno           Pendiente

  Nombre del Application Pool       Administrador            Interno           Obligatorio para
                                                                               ACL

  Identidad del Application Pool    Administrador/estándar   Interno           Pendiente
                                    IIS                                        

  Ruta física de aplicación         Administrador            Interno           Pendiente

  Binding/host/puerto/certificado   Administrador/PKI        Interno           Pendiente

  UNC de Qumulo                     IT                       Sensible          Obligatorio
                                                             operativo         

  Ruta del archivo DPAPI            Administrador            Sensible          Obligatorio
                                                             operativo         

  Domain                            IT/Qumulo                Sensible          Obligatorio

  UserName                          IT/Qumulo                Sensible          Obligatorio

  Password                          IT/Qumulo                Secreto           Obligatorio,
                                                                               entrada segura

  Servidor/base SQL                 DBA                      Interno           Confirmar contra
                                                                               código

  Clave de conexión SQL             Código/contrato          Interno           Confirmar contra
                                                                               código

  Entorno                           Administrador            Interno           Production

  Permisos E1--E5                   IT/administrador         Evidencia de      Obligatorio para
                                                             control           aceptación
  ----------------------------------------------------------------------------------------------

------------------------------------------------------------------------

## 16. Confirmaciones externas E1--E5 y criterios de aceptación

La autorización de implementación no depende de tener ya estos permisos;
la aceptación de producción sí.

-   **E1:** IT confirma Share Read + Change y filesystem Modify,
    incluyendo Delete y Delete subfolders/files, en la raíz y todo su
    contenido.
-   **E2:** IT/Qumulo confirma que la cuenta local autentica por SMB con
    NTLMv2 y entrega el formato exacto de cuenta.
-   **E3:** IT confirma que la política NTLM saliente del host IIS
    permite la autenticación al cluster.
-   **E4:** IT confirma TCP 445, dialecto SMB y requisitos de
    signing/encryption.
-   **E5:** el administrador confirma el nombre del Application Pool y
    la ubicación del archivo de credenciales.

La aceptación de producción exige además las pruebas de §14 del Storage
Access Contract: aplicación iniciada en Qumulo-account mode,
`/health/ready` Healthy, operaciones de aplicación sobre un poster de
prueba, migración idempotente, simulacro de rotación y logs sin datos
prohibidos.

**Build PASS y Tests PASS no son aceptación.** Las pruebas con mocks o
filesystem local no prueban conectividad SMB real.

------------------------------------------------------------------------

## 17. Datos que siguen pendientes y no se deben inventar

1.  Versión exacta de Windows Server y baseline institucional.
2.  Nombre definitivo del sitio IIS.
3.  Nombre definitivo del Application Pool.
4.  Identidad exacta del Application Pool.
5.  Ruta física de instalación.
6.  Binding, hostname, puerto y certificado HTTPS.
7.  UNC real del share Qumulo.
8.  Ruta aprobada para el archivo DPAPI.
9.  Domain y formato de cuenta local Qumulo.
10. Confirmación de E1--E5.
11. Servidor/instancia SQL y principal Windows efectivo.
12. Clave y formato exactos de la configuración SQL en el código final.
13. Forma de distribución, firma, elevación, logging y actualización del
    instalador.
14. Qué operaciones podrá ejecutar automáticamente el instalador y
    cuáles solo podrá verificar, según las políticas de IT.

No completar estos datos con ejemplos convertidos en defaults. Deben ser
proporcionados por el administrador/IT y validados contra el código y
los contratos.

------------------------------------------------------------------------

## 18. Fuentes

### Documentos del proyecto

-   `docs/Poster_Print_Request_System_Roadmap_v4_Inventory.docx` ---
    autoridad de orden y estado.
-   `docs/Storage_Access_Contract_v1.docx` --- especialmente §§2--15.
-   `docs/Shared_Storage_Contract_v1.docx` --- especialmente §§3--10 y
    §12.
-   Contrato de inventario de consumibles --- solo para su propio
    ámbito.

### Documentación oficial Microsoft

-   [Host ASP.NET Core on Windows with IIS --- .NET
    10](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
-   [Publish an ASP.NET Core app to
    IIS](https://learn.microsoft.com/en-us/aspnet/core/tutorials/publish-to-iis)
-   [Configuration in ASP.NET Core --- .NET
    10](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)

------------------------------------------------------------------------

## 19. Historial de ejecución

Completar esta sección durante la instalación real, sin incluir
contraseñas, tokens, contenido DPAPI ni rutas sensibles en el documento
que se distribuya.

  -----------------------------------------------------------------------------------
  Fecha          Paso              Responsable    Resultado      Evidencia/registro
                                                                 institucional
  -------------- ----------------- -------------- -------------- --------------------
  ---            IIS instalado     ---            Pendiente      ---

  ---            Hosting Bundle    ---            Pendiente      ---
                 instalado                                       

  ---            Sitio y           ---            Pendiente      ---
                 Application Pool                                
                 configurados                                    

  ---            Paquete publicado ---            Pendiente      ---
                 desplegado                                      

  ---            SQL Integrated    ---            Pendiente      ---
                 Authentication                                  
                 validada                                        

  ---            E1--E5            ---            Pendiente      ---
                 confirmados                                     

  ---            Directorio DPAPI  ---            Pendiente      ---
                 y ACL creados                                   

  ---            Archivo DPAPI     ---            Pendiente      ---
                 aprovisionado                                   

  ---            Variables IIS     ---            Pendiente      ---
                 configuradas                                    

  ---            Application Pool  ---            Pendiente      ---
                 reciclado                                       

  ---            `/health/ready`   ---            Pendiente      ---
                 validado                                        

  ---            Pruebas SMB y     ---            Pendiente      ---
                 funcionales                                     
                 ejecutadas                                      

  ---            Simulacro de      ---            Pendiente      ---
                 rotación                                        

  ---            Aceptación de     ---            Pendiente      ---
                 producción                                      
  -----------------------------------------------------------------------------------

**Fin del documento.**
