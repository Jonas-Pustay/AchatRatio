window.achatRatio = {
    // ── Images ─────────────────────────────────────────────
    /**
     * Lit l'image choisie dans <input type="file"> (repéré par son id), la
     * redimensionne et renvoie un data-URI JPEG compressé. Tout se passe
     * côté JS : seul le résultat (~60 Ko) traverse l'interop.
     */
    async redimensionnerFichier(idInput, dimMax = 512, qualite = 0.75) {
        const input = document.getElementById(idInput);
        if (!input || !input.files || input.files.length === 0) return null;
        const fichier = input.files[0];

        const url = URL.createObjectURL(fichier);
        try {
            const img = await new Promise((resoudre, rejeter) => {
                const i = new Image();
                i.onload = () => resoudre(i);
                i.onerror = () => rejeter(new Error("Image illisible"));
                i.src = url;
            });

            const echelle = Math.min(1, dimMax / Math.max(img.width, img.height));
            const canvas = document.createElement("canvas");
            canvas.width = Math.max(1, Math.round(img.width * echelle));
            canvas.height = Math.max(1, Math.round(img.height * echelle));

            const ctx = canvas.getContext("2d");
            ctx.fillStyle = "#fff";   // fond blanc : le JPEG ne gère pas la transparence
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.drawImage(img, 0, 0, canvas.width, canvas.height);

            return canvas.toDataURL("image/jpeg", qualite);
        } finally {
            URL.revokeObjectURL(url);
        }
    },

    // ── Export manuel (téléchargement) ─────────────────────
    telechargerFichier(nom, contenu, typeMime = "application/json") {
        const blob = new Blob([contenu], { type: typeMime });
        const url = URL.createObjectURL(blob);
        const lien = document.createElement("a");
        lien.href = url;
        lien.download = nom;
        document.body.appendChild(lien);
        lien.click();
        lien.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
};
