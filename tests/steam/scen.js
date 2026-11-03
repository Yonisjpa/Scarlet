// Escenarios de descargas tal como los manda Steam (formas reales de log-8/log-9 y del código de su pantalla de descargas).
// Cada uno: grupos de lista (itemsCb) y avances (ovCb) en el orden en que llegan.
const z = { bytes_in_progress: 0, bytes_total: 0, estimated_time_remaining_sec: -1 };
const ph = (o = {}) => [0, 1, 2, 3, 4, 5, 6].map(n => o[n] ? { bytes_in_progress: o[n][0], bytes_total: o[n][1], estimated_time_remaining_sec: o[n][2] ?? -1 } : z);
const uti = (content, workshop, shader) => [content || { has_update: false, completed: false, progress: ph() }, workshop || { has_update: false, completed: false, progress: ph() }, shader || { has_update: false, completed: false, progress: ph() }];
const item = o => Object.assign({ appid: 0, active: false, paused: false, completed: false, deferred_time: 0, queue_index: -1, update_result: 0, update_error: '', completed_time: 0, buildid: 0, target_buildid: 0, launch_on_completion: false, update_type_info: uti() }, o);
const ov = o => Object.assign({ remote_client_id: '0', update_appid: 0, update_is_upload: false, update_network_bytes_per_second: 0, update_state: 'None', update_state_flags: 0, progress: ph(), overall_percent_complete: 0, overall_estimated_time_remaining_sec: -1, update_is_install: false, update_is_workshop: false, update_is_shader: false, paused: false, lan_peer_hostname: '' }, o);
const CP = 1091500;
module.exports = {
  // Fotos 27-oct 20:41–20:42: la consola recibe de la laptop (83 %, 65.6 de 78.8 GB, 14:36); llega DESPUÉS el avance de la laptop (subida).
  lan_dos_dispositivos: {
    items: [[{ remote_client_id: '0', item_data: [item({ appid: CP, active: true, queue_index: 0, target_buildid: 26161058, update_type_info: uti({ has_update: true, completed: false, progress: ph({ 1: [88786681446, 88786681446, 0], 2: [65.6e9, 78.8e9, 876] }), overall_percent_complete: 83, overall_estimated_time_remaining_sec: 876 }) })] },
             { remote_client_id: '77', item_data: [] }]],
    ovs: [ov({ update_appid: CP, update_state: 'Downloading', update_network_bytes_per_second: 16.3e6, update_is_install: true, lan_peer_hostname: 'YONIS', overall_percent_complete: 83, overall_estimated_time_remaining_sec: 876, progress: ph({ 1: [88786681446, 88786681446, 0], 2: [65.6e9, 78.8e9, 876] }) }),
          ov({ remote_client_id: '77', update_appid: CP, update_is_upload: true, update_state: 'Downloading', update_network_bytes_per_second: 21.2e6, lan_peer_hostname: 'Thinkaboutyonis', overall_percent_complete: 0, overall_estimated_time_remaining_sec: 3720, progress: ph({ 2: [92.7e9, 78.8e9, 3720] }) })]
  },
  // Llega una lista solo con el grupo de la otra PC: la de esta consola no se borra.
  lista_solo_remota: {
    items: [[{ remote_client_id: '0', item_data: [item({ appid: CP, active: true, queue_index: 0, target_buildid: 5, update_type_info: uti({ has_update: true, progress: ph({ 2: [1e9, 10e9] }) }) })] }],
            [{ remote_client_id: '77', item_data: [item({ appid: 999, active: true, queue_index: 0 })] }]],
    ovs: [ov({ update_appid: CP, update_state: 'Downloading', update_is_install: true, overall_percent_complete: 10, progress: ph({ 2: [1e9, 10e9] }) })]
  },
  // Después de instalar: Steam baja sombreadores (o workshop) del mismo juego.
  sombreadores: {
    items: [[{ remote_client_id: '0', item_data: [item({ appid: CP, active: true, queue_index: 0, buildid: 26161058, target_buildid: 26161058, update_type_info: uti({ has_update: true, completed: true, progress: ph({ 2: [78.8e9, 78.8e9] }) }, null, { has_update: true, completed: false, progress: ph({ 2: [2e8, 9e8] }) }) })] }]],
    ovs: [ov({ update_appid: CP, update_state: 'Downloading', update_is_shader: true, overall_percent_complete: 22, progress: ph({ 2: [2e8, 9e8] }) })]
  },
  // Actualización de un juego instalado, bajando, con otra en cola.
  actualizacion_y_cola: {
    items: [[{ remote_client_id: '0', item_data: [
      item({ appid: 570, active: true, queue_index: 0, buildid: 100, target_buildid: 120, update_type_info: uti({ has_update: true, progress: ph({ 2: [4e8, 1e9] }) }) }),
      item({ appid: 440, active: false, queue_index: 1, buildid: 7, target_buildid: 8, update_type_info: uti({ has_update: true, progress: ph({ 2: [0, 5e8] }) }) })] }]],
    ovs: [ov({ update_appid: 570, update_state: 'Downloading', update_network_bytes_per_second: 5e6, overall_percent_complete: 40, overall_estimated_time_remaining_sec: 120, progress: ph({ 2: [4e8, 1e9, 120] }) })]
  },
  // Pendientes: sin cola (Steam la pedirá al jugar), programada para después, instalación a medias sin cola (no cuenta) y terminada.
  pendientes: {
    items: [[{ remote_client_id: '0', item_data: [
      item({ appid: 10, queue_index: -1, buildid: 3, target_buildid: 4, update_type_info: uti({ has_update: true, progress: ph({ 2: [0, 2e9] }) }) }),
      item({ appid: 20, queue_index: -1, deferred_time: 1893456000, buildid: 3, target_buildid: 4, update_type_info: uti({ has_update: true, progress: ph({ 2: [0, 1e9] }) }) }),
      item({ appid: 30, queue_index: -1, buildid: 0, target_buildid: 9, update_type_info: uti({ has_update: true, progress: ph({ 2: [5e8, 4e9] }) }) }),
      item({ appid: 40, completed: true, completed_time: 1761600000, buildid: 9, target_buildid: 9, update_type_info: uti({ has_update: true, completed: true, progress: ph({ 2: [1e9, 1e9] }) }) })] }]],
    ovs: [ov({})]
  },
  // Steam la detuvo por un problema (sin espacio) y todo en pausa.
  error_y_pausa: {
    items: [[{ remote_client_id: '0', item_data: [
      item({ appid: 50, paused: true, queue_index: 0, buildid: 0, target_buildid: 2, update_result: 12, update_error: 'Disk write failure', update_type_info: uti({ has_update: true, progress: ph({ 2: [3e9, 6e9] }) }) }),
      item({ appid: 60, queue_index: 1, buildid: 5, target_buildid: 6, update_type_info: uti({ has_update: true, progress: ph({ 2: [0, 1e9] }) }) })] }]],
    ovs: [ov({ paused: true })]
  },
  // Esta consola solo le está pasando un juego a otra PC.
  solo_subida: {
    items: [[{ remote_client_id: '0', item_data: [] }]],
    ovs: [ov({ update_appid: CP, update_is_upload: true, update_state: 'Downloading', overall_percent_complete: 50, progress: ph({ 2: [4e10, 8e10] }) })]
  },
  // Reservando espacio: «X de Y» del paso de reservar (como Steam), el porcentaje aún en 0.
  reservando: {
    items: [[{ remote_client_id: '0', item_data: [item({ appid: CP, active: true, queue_index: 0, target_buildid: 1, update_type_info: uti({ has_update: true, progress: ph({ 1: [1e9, 89e9], 2: [0, 78.8e9] }) }) })] }]],
    ovs: [ov({ update_appid: CP, update_state: 'Preallocating', update_is_install: true, progress: ph({ 1: [1e9, 89e9], 2: [0, 78.8e9] }) })]
  },
  // Versión vieja de Steam: lista sin grupos ni update_type_info.
  formato_viejo: {
    items: [[item({ appid: 292030, active: true, queue_index: 0, downloaded_bytes: 3e9, total_bytes: 1e10, buildid: 0, update_type_info: undefined })]],
    ovs: [ov({ update_appid: 292030, update_state: 'Updating', update_is_install: true, overall_percent_complete: 30, progress: undefined, update_bytes_downloaded: 3e9, update_bytes_to_download: 1e10 })]
  }
};
