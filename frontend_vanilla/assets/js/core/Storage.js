/**
 * Storage.js - IndexedDB wrapper for large data storage
 * Provides much larger quota than localStorage (250MB+)
 */

const DB_NAME = 'DodoChatDB';
const STORE_NAME = 'chat_history';
const DB_VERSION = 1;

export class StorageManager {
    static async _getDB() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(DB_NAME, DB_VERSION);
            
            request.onupgradeneeded = (event) => {
                const db = event.target.result;
                if (!db.objectStoreNames.contains(STORE_NAME)) {
                    db.createObjectStore(STORE_NAME, { keyPath: 'id' });
                }
            };

            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    static async saveHistory(history) {
        const db = await this._getDB();
        const tx = db.transaction(STORE_NAME, 'readwrite');
        const store = tx.objectStore(STORE_NAME);
        
        // Clear old and save new (simplest sync approach for history array)
        // We save the entire history object under a single key 'main_history'
        // OR we save each conversation as a separate record. 
        // Let's save each conversation as a separate record for better performance.
        
        return new Promise((resolve, reject) => {
            // First clear all existing
            const clearReq = store.clear();
            clearReq.onsuccess = () => {
                let count = 0;
                if (history.length === 0) resolve();
                
                history.forEach(item => {
                    const req = store.add(item);
                    req.onsuccess = () => {
                        count++;
                        if (count === history.length) resolve();
                    };
                    req.onerror = () => reject(req.error);
                });
            };
            clearReq.onerror = () => reject(clearReq.error);
        });
    }

    static async loadHistory() {
        try {
            const db = await this._getDB();
            const tx = db.transaction(STORE_NAME, 'readonly');
            const store = tx.objectStore(STORE_NAME);
            
            return new Promise((resolve, reject) => {
                const request = store.getAll();
                request.onsuccess = () => {
                    // Sort by id descending (assuming id is timestamp)
                    const data = request.result || [];
                    resolve(data.sort((a, b) => b.id - a.id));
                };
                request.onerror = () => reject(request.error);
            });
        } catch (error) {
            console.error('Failed to load history from IndexedDB:', error);
            return [];
        }
    }

    static async saveConversation(conversation) {
        if (!conversation || !conversation.id) return;
        try {
            const db = await this._getDB();
            const tx = db.transaction(STORE_NAME, 'readwrite');
            const store = tx.objectStore(STORE_NAME);
            
            return new Promise((resolve, reject) => {
                const request = store.put(conversation);
                request.onsuccess = () => resolve();
                request.onerror = () => reject(request.error);
            });
        } catch (error) {
            console.error('Failed to save individual conversation to IndexedDB:', error);
        }
    }
}
