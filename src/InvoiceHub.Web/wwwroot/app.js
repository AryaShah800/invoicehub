window.invoicehub = {
    // Saves a stream from the server as a file, using a temporary link (works without a popup).
    download: async (fileName, streamReference) => {
        const buffer = await streamReference.arrayBuffer();
        const url = URL.createObjectURL(new Blob([buffer], { type: "application/pdf" }));
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    },
};
