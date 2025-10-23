from scipy.signal import butter, filtfilt

def lowpass_filter(signal, fs=30.0, fc=2, order=2):
    """Butterworth low-pass filter."""
    # Conception du filtre
    b, a = butter(order, fc/(fs/2), btype='low')
    
    # Application du filtre (sans déphasage)
    y_filtered = filtfilt(b, a, signal)

    return y_filtered

def highpass_filter(signal, fs=30.0, fc=0.3, order=2):
    """Butterworth high-pass filter."""
    # Conception du filtre
    b, a = butter(order, fc/(fs/2), btype='high')
    
    # Application du filtre (sans déphasage)
    y_filtered = filtfilt(b, a, signal)

    return y_filtered