using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SOEA.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Migración de DATOS (sin cambio de esquema): unifica el catálogo de <c>BloqueTiempos</c> con
    /// los Ids determinísticos de <see cref="SOEA.Domain.Services.GrillaInstitucional"/>.
    ///
    /// Hasta ahora convivían dos catálogos de la misma grilla de 87 bloques (5 días × 16 horas +
    /// sábado × 7 horas), con esquemas de Id incompatibles: esta tabla se sembraba con
    /// <c>Guid.NewGuid()</c> (<c>BloqueTiempoSeeder</c>, ya corregido en este mismo cambio), y el
    /// pipeline de generación regenera la grilla en memoria en cada request con un Id
    /// determinístico — hash MD5 de "bloque-{dia}-{hora}" (<c>GrillaInstitucional</c>). Como no
    /// hay una FK real entre <c>Sesiones.bloque_tiempo_id</c> y esta tabla, ambos IDs convivían
    /// sin que nada fallara ruidosamente: cualquier búsqueda contra el catálogo de BD
    /// (<c>AsignarDocenteSesionService</c>, <c>CrearSesionManualService</c>) simplemente no
    /// encontraba el bloque de una sesión generada por el pipeline y seguía de largo (<c>continue</c>).
    /// En la práctica: HC-I01 (solape de docente, 409 esperado) nunca disparaba para sesiones
    /// generadas, HC-I02 (disponibilidad) advertía siempre, y HC-S01/HC-SEP en creación manual de
    /// sesiones no tenían nada real contra qué comparar. Confirmado contra una base con datos
    /// reales: los 87 bloques sembrados no coincidían con ninguno de los 87 deterministas, y 2
    /// sesiones ya generadas tenían un <c>bloque_tiempo_id</c> que no existía en esta tabla.
    ///
    /// Estrategia (segura frente al FK real que sí existe de
    /// <c>DisponibilidadDocente.BloqueTiempoId</c> hacia esta tabla — nunca se actualiza un Id ya
    /// referenciado in situ):
    ///   1. Insertar los 87 bloques con su Id determinístico si todavía no existen (una base ya
    ///      corregida, o una sesión que ya apuntaba al Id determinístico sin que la fila existiera
    ///      — como las 2 huérfanas detectadas — queda resuelta aquí mismo).
    ///   2. Mapear cada bloque VIEJO (Id aleatorio) a su equivalente determinista por (día, hora
    ///      de inicio).
    ///   3. Repuntar `Sesiones` y `DisponibilidadDocente` de los Ids viejos a los nuevos.
    ///   4. Borrar los bloques viejos: para entonces ya no los referencia nadie.
    /// </summary>
    public partial class M10_UnificarCatalogoBloques : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- 1. Catálogo canónico: los 87 (Id, día, hora_inicio, hora_fin) que genera
                --    GrillaInstitucional.GenerarBloques() — mismos valores, calculados una vez al
                --    escribir esta migración porque no dependen de ningún dato de la BD.
                CREATE TEMP TABLE bloque_canonico (id uuid, dia text, hora_inicio time, hora_fin time) ON COMMIT DROP;
                INSERT INTO bloque_canonico (id, dia, hora_inicio, hora_fin) VALUES
                    ('924ec4ef-4cf5-8f14-b1ff-a110943da53f', 'Lunes', '06:00:00'::time, '07:00:00'::time),
                    ('0b123c7e-e40b-f689-deec-37aa7e2fa865', 'Lunes', '07:00:00'::time, '08:00:00'::time),
                    ('238a2d68-511e-2194-d73c-eaad581d8deb', 'Lunes', '08:00:00'::time, '09:00:00'::time),
                    ('7fbfcba1-f701-501b-bbf6-78183bf11231', 'Lunes', '09:00:00'::time, '10:00:00'::time),
                    ('9a36c837-e86e-4eb0-b748-a7eed05c40ab', 'Lunes', '10:00:00'::time, '11:00:00'::time),
                    ('0b71be60-925b-35cf-2e68-e8513324cb9b', 'Lunes', '11:00:00'::time, '12:00:00'::time),
                    ('fed50a73-3cc9-205a-1152-5ad967bf7842', 'Lunes', '12:00:00'::time, '13:00:00'::time),
                    ('12ddb239-ca47-2c1d-59bd-a7e5f408408d', 'Lunes', '13:00:00'::time, '14:00:00'::time),
                    ('e8ee2b66-ce9a-a117-0340-57811b408a26', 'Lunes', '14:00:00'::time, '15:00:00'::time),
                    ('f5b05252-e94f-7ca5-9871-1676c61aee91', 'Lunes', '15:00:00'::time, '16:00:00'::time),
                    ('f7c35a2d-17c1-711f-2d2c-9d22c3eff300', 'Lunes', '16:00:00'::time, '17:00:00'::time),
                    ('93693060-bdbb-821a-b80c-820691b8f36c', 'Lunes', '17:00:00'::time, '18:00:00'::time),
                    ('5857b9bc-178a-8d7d-f4da-4cff2e10974e', 'Lunes', '18:00:00'::time, '19:00:00'::time),
                    ('b15af20f-6c62-58f0-8103-33498aff3455', 'Lunes', '19:00:00'::time, '20:00:00'::time),
                    ('c4f105b2-592b-fe3b-3fa4-4deae3a0d6c1', 'Lunes', '20:00:00'::time, '21:00:00'::time),
                    ('1154dbf5-7b44-318c-7c3f-1a12e7f73850', 'Lunes', '21:00:00'::time, '22:00:00'::time),
                    ('2ca8d657-6b11-adcb-b99d-a5e88d34e313', 'Martes', '06:00:00'::time, '07:00:00'::time),
                    ('94db770a-a3c1-3ac4-8382-954377447d4e', 'Martes', '07:00:00'::time, '08:00:00'::time),
                    ('aa762d25-6632-981b-6d90-15e6823be2ed', 'Martes', '08:00:00'::time, '09:00:00'::time),
                    ('37385bc8-d5b5-57a0-6c61-a81bb387604d', 'Martes', '09:00:00'::time, '10:00:00'::time),
                    ('88b45e8c-46ee-571b-526b-ef0245468b8e', 'Martes', '10:00:00'::time, '11:00:00'::time),
                    ('68e66663-5068-7439-3180-a6db4958887e', 'Martes', '11:00:00'::time, '12:00:00'::time),
                    ('907508c8-9318-8668-07b6-e9b918cd2d72', 'Martes', '12:00:00'::time, '13:00:00'::time),
                    ('b5dbb693-2275-7d49-bf4e-15df1b22e0ad', 'Martes', '13:00:00'::time, '14:00:00'::time),
                    ('c8811f3a-a53a-2b35-6e30-d8dafa2e080f', 'Martes', '14:00:00'::time, '15:00:00'::time),
                    ('2d5d6261-7f46-3f15-deec-67b7bbe6c3fe', 'Martes', '15:00:00'::time, '16:00:00'::time),
                    ('47e9501e-841e-dbfb-72fe-ecd7523b434f', 'Martes', '16:00:00'::time, '17:00:00'::time),
                    ('1fd6e02c-75a1-a346-3b0e-1a89c50bc922', 'Martes', '17:00:00'::time, '18:00:00'::time),
                    ('407a75a7-8356-7ecf-56a4-364233ab2b64', 'Martes', '18:00:00'::time, '19:00:00'::time),
                    ('3778fb04-0225-4aa4-cd7d-0a3e92dcd69e', 'Martes', '19:00:00'::time, '20:00:00'::time),
                    ('55d47cff-6c7f-ea72-9f33-d0acd717042f', 'Martes', '20:00:00'::time, '21:00:00'::time),
                    ('495fe031-014d-46aa-ea20-a942d4c6588c', 'Martes', '21:00:00'::time, '22:00:00'::time),
                    ('3d919d74-6b5f-c19e-1e0a-23c991e0e8b1', 'Miercoles', '06:00:00'::time, '07:00:00'::time),
                    ('c1a46959-e4ac-39d2-393b-dfeab20b45a4', 'Miercoles', '07:00:00'::time, '08:00:00'::time),
                    ('2a89f700-21f7-436b-28b7-6e6053c1278f', 'Miercoles', '08:00:00'::time, '09:00:00'::time),
                    ('fe4b6ec0-1026-2ef5-36e9-0fe80fce0597', 'Miercoles', '09:00:00'::time, '10:00:00'::time),
                    ('aa2ac659-bb13-7190-6fd2-5bf5198a7d96', 'Miercoles', '10:00:00'::time, '11:00:00'::time),
                    ('746ea7e0-309b-b573-01c1-0ae2030deeab', 'Miercoles', '11:00:00'::time, '12:00:00'::time),
                    ('bd0d2fc6-b54f-62c7-e797-41d76ba71af3', 'Miercoles', '12:00:00'::time, '13:00:00'::time),
                    ('89291bc5-29bc-9c9c-9d53-efb9ef075169', 'Miercoles', '13:00:00'::time, '14:00:00'::time),
                    ('f9dade78-b595-b252-577f-ea5e73ba0b90', 'Miercoles', '14:00:00'::time, '15:00:00'::time),
                    ('606b5383-b418-df4a-a951-06273b6317d2', 'Miercoles', '15:00:00'::time, '16:00:00'::time),
                    ('2bfad50a-0b2e-a187-0627-ac247224ce4d', 'Miercoles', '16:00:00'::time, '17:00:00'::time),
                    ('237c2397-257b-fb3c-4349-b0606e95e749', 'Miercoles', '17:00:00'::time, '18:00:00'::time),
                    ('7aa281bb-8575-8dcb-6806-a518cf838d06', 'Miercoles', '18:00:00'::time, '19:00:00'::time),
                    ('1b2ce92f-ffa7-4103-9dab-bf6f00fee52e', 'Miercoles', '19:00:00'::time, '20:00:00'::time),
                    ('a5723420-7c6d-5230-6ce9-8bb708a67790', 'Miercoles', '20:00:00'::time, '21:00:00'::time),
                    ('25813c9b-2425-7b76-bc78-57928e592e3c', 'Miercoles', '21:00:00'::time, '22:00:00'::time),
                    ('0bf18388-1fbb-ab19-8ce2-1b7b90909b03', 'Jueves', '06:00:00'::time, '07:00:00'::time),
                    ('3bb816d6-5d02-25a5-bbab-63dff35fbf39', 'Jueves', '07:00:00'::time, '08:00:00'::time),
                    ('4bd1c679-eb19-6006-3939-da80e6b83679', 'Jueves', '08:00:00'::time, '09:00:00'::time),
                    ('3b0ca606-57f7-54ad-fb94-86614d99043e', 'Jueves', '09:00:00'::time, '10:00:00'::time),
                    ('9b343435-67c8-e285-245a-2185b8faf726', 'Jueves', '10:00:00'::time, '11:00:00'::time),
                    ('93311d88-0190-b7d3-8ca8-9149b2ffff74', 'Jueves', '11:00:00'::time, '12:00:00'::time),
                    ('978bbc9e-4f83-715b-0341-5b24fda37cbd', 'Jueves', '12:00:00'::time, '13:00:00'::time),
                    ('f9cb01b6-ac17-6026-d4fe-23984034db4e', 'Jueves', '13:00:00'::time, '14:00:00'::time),
                    ('3f850ba0-c257-f1cb-b565-1c34fed560ed', 'Jueves', '14:00:00'::time, '15:00:00'::time),
                    ('5e58f2e1-8d4d-e3d0-6f73-0f8d1b686b76', 'Jueves', '15:00:00'::time, '16:00:00'::time),
                    ('4a7166fd-7bd9-da20-31af-32dd68c5ba55', 'Jueves', '16:00:00'::time, '17:00:00'::time),
                    ('6f022c98-c6d7-86f3-1a6a-032735b8b916', 'Jueves', '17:00:00'::time, '18:00:00'::time),
                    ('f2cb14e5-e5d7-8166-db56-9153c670ee7a', 'Jueves', '18:00:00'::time, '19:00:00'::time),
                    ('08d94570-457a-07d6-cdf1-337d3ee2aa56', 'Jueves', '19:00:00'::time, '20:00:00'::time),
                    ('a2d86bd4-f930-d16a-23d6-73f797a6f378', 'Jueves', '20:00:00'::time, '21:00:00'::time),
                    ('1610b67c-b2f2-47a6-bf7c-c40ff94f7b95', 'Jueves', '21:00:00'::time, '22:00:00'::time),
                    ('1a234d2b-6b72-365f-d4f8-f2ee34a4a775', 'Viernes', '06:00:00'::time, '07:00:00'::time),
                    ('c3f4ea74-6054-275c-81cd-2c488ea2f869', 'Viernes', '07:00:00'::time, '08:00:00'::time),
                    ('31f8d775-1c61-fd39-5dcd-9069fe07f399', 'Viernes', '08:00:00'::time, '09:00:00'::time),
                    ('08d6e4ce-1da5-9eb7-6520-a2caedd9296a', 'Viernes', '09:00:00'::time, '10:00:00'::time),
                    ('43e8f9fc-8077-518d-3a95-16b282a255f3', 'Viernes', '10:00:00'::time, '11:00:00'::time),
                    ('665378a4-0357-912a-d0c2-41745b6376c2', 'Viernes', '11:00:00'::time, '12:00:00'::time),
                    ('f6f87f3c-d3c9-edc0-0d43-3ee7d590568f', 'Viernes', '12:00:00'::time, '13:00:00'::time),
                    ('f76d061f-b634-8b04-3095-a0a85d43aca4', 'Viernes', '13:00:00'::time, '14:00:00'::time),
                    ('93c29cf7-e62d-97f0-a79f-d2e157e8e118', 'Viernes', '14:00:00'::time, '15:00:00'::time),
                    ('48bfe84f-1135-e1ff-7905-aa6b485b4391', 'Viernes', '15:00:00'::time, '16:00:00'::time),
                    ('b026b9f5-da0d-2288-5cd9-fb0dbd3d4a17', 'Viernes', '16:00:00'::time, '17:00:00'::time),
                    ('746a6ec9-0db5-64ce-6d2c-e909d914443e', 'Viernes', '17:00:00'::time, '18:00:00'::time),
                    ('346c1c3f-e1c4-75e6-49bc-847fd41cecad', 'Viernes', '18:00:00'::time, '19:00:00'::time),
                    ('feae3b47-8f52-b8e6-9629-eee2175f15a5', 'Viernes', '19:00:00'::time, '20:00:00'::time),
                    ('f7f6a603-7a45-3ef0-f72a-b1b5f0dd8905', 'Viernes', '20:00:00'::time, '21:00:00'::time),
                    ('91d2c0fa-96cd-d1ff-4abe-2f83b58c7fe7', 'Viernes', '21:00:00'::time, '22:00:00'::time),
                    ('577956bb-0987-13fa-f225-6d20157dc162', 'Sábado', '06:00:00'::time, '07:00:00'::time),
                    ('89e56ca5-2803-3c99-12f1-4560b7a40b60', 'Sábado', '07:00:00'::time, '08:00:00'::time),
                    ('f27de8ee-6475-b344-444a-c98d25646426', 'Sábado', '08:00:00'::time, '09:00:00'::time),
                    ('6ef63134-f605-955a-781e-bb731ba95d63', 'Sábado', '09:00:00'::time, '10:00:00'::time),
                    ('d1aa1b7c-d356-ee00-18f0-5b1395baf89e', 'Sábado', '10:00:00'::time, '11:00:00'::time),
                    ('5f49d41b-8587-c4e6-ce05-e5127bdd0b6f', 'Sábado', '11:00:00'::time, '12:00:00'::time),
                    ('46d905c4-56e7-f627-4a76-1da3250ed5f4', 'Sábado', '12:00:00'::time, '13:00:00'::time);

                -- 2. Insertar los bloques canónicos que todavía no existan con ese Id exacto.
                INSERT INTO "BloqueTiempos" (id, dia, hora_inicio, hora_fin)
                SELECT c.id, c.dia, c.hora_inicio, c.hora_fin
                FROM bloque_canonico c
                WHERE NOT EXISTS (SELECT 1 FROM "BloqueTiempos" b WHERE b.id = c.id);

                -- 3. Mapa Id-viejo → Id-nuevo, por coincidencia de (día, hora_inicio). Un bloque
                --    cuyo Id ya es el determinístico no necesita mapearse (b.id <> c.id).
                CREATE TEMP TABLE bloque_id_map (old_id uuid, new_id uuid) ON COMMIT DROP;
                INSERT INTO bloque_id_map (old_id, new_id)
                SELECT b.id, c.id
                FROM "BloqueTiempos" b
                JOIN bloque_canonico c ON c.dia = b.dia AND c.hora_inicio = b.hora_inicio
                WHERE b.id <> c.id;

                -- 4. Repuntar Sesiones (sin FK real hacia BloqueTiempos, pero igual queremos que el
                --    dato deje de estar huérfano) y DisponibilidadDocente (FK real: por eso el
                --    bloque nuevo del paso 2 ya existe antes de tocar esta fila).
                UPDATE "Sesiones" s
                SET bloque_tiempo_id = m.new_id
                FROM bloque_id_map m
                WHERE s.bloque_tiempo_id = m.old_id;

                UPDATE "DisponibilidadDocente" d
                SET "BloqueTiempoId" = m.new_id
                FROM bloque_id_map m
                WHERE d."BloqueTiempoId" = m.old_id;

                -- 5. Borrar los bloques viejos: a esta altura nada los referencia.
                DELETE FROM "BloqueTiempos" b
                USING bloque_id_map m
                WHERE b.id = m.old_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin operación a propósito: los Ids aleatorios originales no se conservan en ningún
            // lado (esa es justamente la causa del bug), así que no hay a qué volver. Revertir
            // "de vuelta a Ids aleatorios" solo reintroduciría el split-brain que esta migración
            // corrige — nunca es la operación correcta, ni siquiera como rollback de emergencia.
        }
    }
}
